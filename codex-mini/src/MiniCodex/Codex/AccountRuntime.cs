using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime(AppController controller, Guid profileId, Action<string> claimStateDirectory, DraftStore drafts) : ObservableObject, IAsyncDisposable
{
    private CodexClient? _client;
    private readonly SemaphoreSlim _connect = new(1, 1);
    private readonly SemaphoreSlim _authentication = new(1, 1);
    private readonly Dictionary<Guid, ConversationState> _conversations = [];
    private readonly Dictionary<Guid, int> _pinnedConversations = [];
    private readonly Dictionary<object, Guid> _viewedConversations = [];
    public void PinConversation(Guid id) => _pinnedConversations[id] = _pinnedConversations.GetValueOrDefault(id) + 1;
    public void UnpinConversation(Guid id) { var count = _pinnedConversations.GetValueOrDefault(id) - 1; if (count <= 0) _pinnedConversations.Remove(id); else _pinnedConversations[id] = count; }
    public void SetViewedConversation(object view, Guid? chatId)
    {
        if (chatId is not { } id) { _viewedConversations.Remove(view); return; }
        _viewedConversations[view] = id;
        SetCompletionUnread(id, false);
    }
    private void SetCompletionUnread(Guid chatId, bool unread)
    {
        try { controller.SetChatUnreadCompletion(ProfileId, chatId, unread); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { Report(new IOException("Could not save the chat's completion status. " + error.Message, error)); }
    }
    private readonly Dictionary<(Guid Project, bool Archived), (string Scope, string? Cursor)> _listCursors = [];
    private readonly SemaphoreSlim _historyLists = new(1, 1);
    private string? _loginId;
    private string? _loginUrl;
    private DateTimeOffset? _usageUpdated;
    private JsonElement _rateLimits;
    private long? _availableResetCount;
    private string _plan = "Plan unavailable";
    private bool _disposed;
    private bool _initialized;
    private DispatcherTimer? _usageTimer;
    private DispatcherTimer? _usagePollTimer;
    private bool _refreshingUsage;
    public Guid ProfileId { get; } = profileId;
    public string Status { get; private set; } = "Not connected";
    public string Identity { get; private set; } = "Not signed in";
    public string Notice { get; private set; } = "";
    public string LoginHint { get; private set; } = "";
    public bool SignedIn { get; private set; }
    public AccountAuthentication? Authentication { get; private set; }
    public string HomePath => CodexInstallation.Home(controller.Profile(ProfileId));
    public string? StateDirectory { get; private set; }
    public bool Connected => _initialized && _client?.IsAlive == true;
    public bool LoginPending => _loginId is not null;
    private bool HasNonProjectChatWork => InstallingExtensions || _predictionRequests.Count > 0 || _conversations.Values.Any(chat => chat.Busy || chat.Uncertain || chat.Steering || chat.QueueDispatching || chat.PausingForConflict || chat.ContinuingConflict);
    public bool HasWork => HasNonProjectChatWork || _projectChatOperations.Count > 0 || _archivingSessions.Count > 0 || _deletingSessions.Count > 0;
    public bool HasUncertainWork => _conversations.Values.Any(chat => chat.Uncertain);
    public int RunningCount => _conversations.Values.Count(chat => chat.Busy || chat.Uncertain);
    public IEnumerable<string> ActiveFolders => _conversations.Values.Where(chat => chat.Busy || chat.Uncertain).SelectMany(chat => chat.ActiveFolders);
    public int QueuedCount => drafts.For(ProfileId).Queues.Values.Sum(queue => queue.Count);
    public string TaskSummary => (InstallingExtensions ? "Installing extension" : HasUncertainWork ? "Task outcome unknown" : Approvals.Count > 0 ? $"{Approvals.Count} need review" : RunningCount > 0 ? $"{RunningCount} task{(RunningCount == 1 ? "" : "s")} running" : _predictionRequests.Count > 0 ? "Predicting next message" : "No active tasks") + (QueuedCount > 0 ? $" · {QueuedCount} queued" : "");
    public string Usage => _rateLimits.ValueKind == JsonValueKind.Object ? WindowUsage(_rateLimits.Get("primary")) + "  |  " + WindowUsage(_rateLimits.Get("secondary")) : "— · —  |  — · —";
    public long? AvailableResetCount => _availableResetCount;
    public bool HasResetCount => AvailableResetCount is not null;
    public string ResetLabel => AvailableResetCount is { } count ? $"{count} rst" : "";
    public string Credits
    {
        get
        {
            var credits = _rateLimits.Get("credits");
            if (credits.Get("unlimited").ValueKind == JsonValueKind.True) return "Unlimited credits";
            var balance = credits.Get("balance");
            if (balance.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || !decimal.TryParse(balance.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return "Credits unavailable";
            return decimal.Round(value, 1, MidpointRounding.AwayFromZero).ToString("F1", CultureInfo.InvariantCulture) + " credits remaining";
        }
    }
    public string UsageTooltip => _plan + (_usageUpdated is { } time ? " · Updated " + time.ToLocalTime().ToString("g") : " · Usage not loaded") + "\nPercent remaining · time until reset"
        + (_availableResetCount is { } count ? $"\nAvailable rate-limit resets: {count}" : "");
    public ObservableCollection<CodexModel> Models { get; } = [];
    public ObservableCollection<PendingApproval> Approvals { get; } = [];
    public event EventHandler? Updated;
    public event Action<string?>? SessionListChanged;
    public event Action<ConversationState, string>? TaskCompleted;
    public event Action<PendingApproval>? ApprovalRequested;
    public bool IsSessionRunning(Guid? chatId, string? threadId) => (chatId is { } id ? _conversations.GetValueOrDefault(id) : Find(threadId)) is { Busy: true, Uncertain: false };

    private void Notify()
    {
        if (_usagePollTimer is { } timer)
        {
            if (!Connected || !SignedIn || _refreshingUsage || _disposed) timer.Stop();
            else
            {
                var interval = TimeSpan.FromMinutes(_conversations.Values.Any(chat => chat.Busy && !chat.Uncertain) ? 1 : 10);
                if (timer.Interval != interval) { timer.Stop(); timer.Interval = interval; }
                if (!timer.IsEnabled) timer.Start();
            }
        }
        Changed(string.Empty);
        Updated?.Invoke(this, EventArgs.Empty);
    }
    public void Report(Exception error) { Notice = Wire.Short(error.Message); Notify(); }

    public ConversationState Conversation(ChatState chat)
    {
        if (!_conversations.TryGetValue(chat.Id, out var state))
        {
            foreach (var old in _conversations.Values.Where(item => !item.Busy && !item.Uncertain && !item.Steering && !item.QueueDispatching && item.ConflictPause is null && !item.ContinuingConflict && item.Queue.Count == 0 && !_pinnedConversations.ContainsKey(item.LocalId) && item.LocalId != chat.Id).Take(Math.Max(0, _conversations.Count - 5)).ToArray()) _conversations.Remove(old.LocalId);
            var queue = drafts.Queue(ProfileId, chat.Id);
            _conversations.Add(chat.Id, state = new ConversationState(chat.Id) { ThreadId = chat.CodexThreadId, ConflictPause = chat.ConflictPause, Queue = queue, QueuePaused = queue.Count > 0 || chat.ConflictPause is not null,
                QueueNotice = queue.Count > 0 ? "Recovered queue is paused. Check saved history before resuming; a pending message may already have been sent." : "" });
            state.ConflictBlocked = state.ConflictPause is { } pause && ConflictIsBlocked?.Invoke(pause) == true;
        }
        return state;
    }
    private ConversationState? Find(string? threadId) => threadId is null ? null : _conversations.Values.FirstOrDefault(chat => chat.ThreadId == threadId);

    public Task ConnectAsync() => ConnectAsync(true);

    public async Task ConnectAsync(bool loadAccountDetails, bool recordActivity = true)
    {
        RequireNoExtensionInstall();
        if (recordActivity) Touch();
        await _connect.WaitAsync();
        var starting = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RequireNoExtensionInstall();
            if (Connected)
            {
                if (loadAccountDetails && SignedIn && Models.Count == 0) { await LoadModelsAsync(); await RefreshUsageAsync(); }
                return;
            }
            if (_client is not null && (HasNonProjectChatWork || _projectChatOperations.Values.Any(operation => operation.Started) || _archivingSessions.Values.Any(operation => operation.Started) || _deletingSessions.Values.Any(operation => operation.Started))) throw new IOException("The connection was lost during work. Choose Reconnect to explicitly stop the old process before recovery.");
            starting = true; _initialized = false;
            if (_client is not null) { await _client.DisposeAsync(); _client = null; }
            Status = "Connecting"; Notice = ""; SignedIn = false;
            foreach (var approval in Approvals) CloseQuestions(approval);
            Approvals.Clear(); Notify();
            var start = await CodexInstallation.PrepareAsync(controller.State.CodexExecutable, controller.Profile(ProfileId), controller.State.Profiles);
            CodexClient? connection = null;
            connection = new CodexClient(start,
                message => Application.Current.Dispatcher.InvokeAsync(() => { if (ReferenceEquals(_client, connection)) OnMessage(connection!, message); }).Task,
                reason => Application.Current.Dispatcher.InvokeAsync(() => { if (ReferenceEquals(_client, connection)) OnDisconnected(reason); }));
            _client = connection;
            await connection.CallAsync("initialize", new { clientInfo = new { name = "mini_codex", title = "Mini Codex", version = "0.2.0" }, capabilities = new { experimentalApi = true } });
            await connection.NotifyAsync("initialized", new { });
            var home = FolderPaths.Canonicalize(HomePath);
            var stateDirectory = home;
            if (controller.Profile(ProfileId).ExistingCodexHome is not null)
            {
                var effective = await connection.CallAsync("config/read", new { includeLayers = false });
                var configuredState = effective.Get("config").Text("sqlite_home");
                if (!string.IsNullOrWhiteSpace(configuredState)) stateDirectory = FolderPaths.Canonicalize(Path.GetFullPath(configuredState, home));
            }
            claimStateDirectory(stateDirectory); StateDirectory = stateDirectory;
            _initialized = true;
            Status = "Connected";
            Suspended = false;
            if (_usageTimer is null)
            {
                _usageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                _usageTimer.Tick += OnUsageTick;
            }
            if (_usagePollTimer is null)
            {
                _usagePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
                _usagePollTimer.Tick += OnUsagePollTick;
            }
            _usageTimer.Start();
            if (loadAccountDetails) await RefreshAccountAsync();
            else await RefreshAuthenticationAsync();
        }
        catch (Exception error)
        {
            if (!starting) throw;
            _initialized = false;
            Status = "Connection failed"; SignedIn = false; Notice = Wire.Short(error.Message);
            _usageTimer?.Stop();
            if (_client is not null) { await _client.DisposeAsync(); _client = null; }
            throw;
        }
        finally { _connect.Release(); Notify(); }
    }

    private CodexClient Client => Connected && _client is { } client ? client : throw new IOException("Codex is disconnected. Choose Reconnect.");

    public async Task StopAfterLossAsync()
    {
        RequireNoExtensionInstall();
        PauseAllQueues("Queue paused after stopping the disconnected account.");
        if (Connected) throw new InvalidOperationException("Use Stop while Codex is connected.");
        var predictionsStopped = CancelPredictionsAsync();
        var previous = _client; _client = null; _initialized = false;
        if (previous is not null) await previous.DisposeAsync();
        await predictionsStopped;
        foreach (var state in _conversations.Values)
        { state.Uncertain = false; state.Submitting = false; state.Compacting = false; state.ContextCompactionItemId = null; state.ClearActivities(); state.TurnId = null; state.Terminal.TrySetResult(); }
        Notify();
    }

    public async Task RestartAfterLossAsync()
    {
        RequireNoExtensionInstall();
        if (Connected) throw new InvalidOperationException("The connection is still active. Stop tasks before restarting.");
        var predictionsStopped = CancelPredictionsAsync();
        var previous = _client; _client = null; _initialized = false;
        if (previous is not null) await previous.DisposeAsync();
        await predictionsStopped;
        foreach (var state in _conversations.Values.Where(chat => chat.Busy || chat.Uncertain))
        { state.Uncertain = true; state.Submitting = false; state.ContextCompactionItemId = null; state.ClearActivities(); state.Terminal.TrySetResult(); }
        await ConnectAsync();
    }

    public async Task RefreshAuthenticationAsync()
    {
        await _authentication.WaitAsync();
        try
        {
            var client = Client;
            var response = await client.CallAsync("account/read", new { refreshToken = false });
            if (!ReferenceEquals(client, _client)) return;
            var account = response.Get("account");
            if (account.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) throw new IOException("Codex did not return an account status.");
            SignedIn = account.ValueKind == JsonValueKind.Object;
            Identity = SignedIn ? account.Text("email") ?? account.Text("type") ?? "Signed in" : "Not signed in";
            Authentication = new AccountAuthentication(SignedIn, Identity, DateTimeOffset.Now);
            _plan = account.Text("planType") ?? "Plan unavailable";
            Status = SignedIn ? "Connected" : "Sign-in required";
            if (SignedIn) { _loginId = null; _loginUrl = null; LoginHint = ""; }
            else { Models.Clear(); _availableResetCount = null; }
            Notify();
        }
        finally { _authentication.Release(); }
    }

    public async Task RefreshAccountAsync()
    {
        await RefreshAuthenticationAsync();
        if (SignedIn) { await LoadModelsAsync(); await RefreshUsageAsync(); }
        Notify();
    }

    public async Task BeginLoginAsync(bool deviceCode)
    {
        await ConnectAsync();
        if (HasWork) throw new InvalidOperationException("Stop this profile's tasks before changing its sign-in.");
        await CancelLoginAsync();
        RequireNoExtensionInstall();
        var result = await Client.CallAsync("account/login/start", new { type = deviceCode ? "chatgptDeviceCode" : "chatgpt" });
        _loginId = result.Text("loginId");
        _loginUrl = result.Text(deviceCode ? "verificationUrl" : "authUrl");
        LoginHint = deviceCode ? "Enter this code in the browser: " + result.Text("userCode") : "Complete sign-in in your browser. Use the identity intended for this profile.";
        Notify();
        if (_loginUrl is not null) OpenLoginBrowser();
        else throw new IOException("Codex did not provide a sign-in URL.");
    }

    public void OpenLoginBrowser()
    {
        if (!Uri.TryCreate(_loginUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host == "chatgpt.com" || uri.Host == "openai.com" || uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Codex returned an unsupported sign-in destination.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    public async Task CancelLoginAsync()
    {
        if (_loginId is { } id && Connected) await Client.CallAsync("account/login/cancel", new { loginId = id }, true);
        _loginId = null; _loginUrl = null; LoginHint = ""; Notify();
    }
    public async Task LogoutAsync()
    {
        if (HasWork) throw new InvalidOperationException("Stop this profile's tasks before signing out.");
        CancelChatTitles();
        PauseAllQueues("Queue paused after signing out. Sign in and resume when ready.");
        await CancelLoginAsync();
        await _authentication.WaitAsync();
        try
        {
            if (HasWork) throw new InvalidOperationException("Stop this profile's tasks before signing out.");
            await Client.CallAsync("account/logout", new { }, true);
            SignedIn = false; Identity = "Not signed in"; Models.Clear(); _rateLimits = default; _availableResetCount = null;
            Authentication = new AccountAuthentication(false, Identity, DateTimeOffset.Now);
            Status = "Sign-in required"; Notify();
        }
        finally { _authentication.Release(); }
    }
    private async Task LoadModelsAsync()
    {
        var results = new List<CodexModel>(); string? cursor = null;
        do
        {
            var page = await Client.CallAsync("model/list", new { limit = 100, cursor });
            foreach (var item in page.Get("data").Items())
            {
                if (item.Text("id") is not { } id) continue;
                var efforts = item.Get("supportedReasoningEfforts").Items().Select(value => value.Text("reasoningEffort")).OfType<string>().ToArray();
                results.Add(new CodexModel(item.Text("model") ?? id, item.Text("displayName") ?? id, efforts, item.Text("defaultReasoningEffort") ?? "medium", item.Get("isDefault").ValueKind == JsonValueKind.True));
            }
            cursor = page.Text("nextCursor");
        } while (cursor is not null && results.Count < 500);
        Models.Clear(); foreach (var model in results) Models.Add(model);
    }
    public async Task RefreshUsageAsync()
    {
        if (_refreshingUsage) return;
        _refreshingUsage = true;
        _usagePollTimer?.Stop();
        var revision = _usageRevision;
        try
        {
            var response = await Client.CallAsync("account/rateLimits/read", new { });
            if (revision == _usageRevision) ApplyUsage(response);
        }
        catch (CodexRpcException) { if (revision == _usageRevision) { _rateLimits = default; _availableResetCount = null; } }
        finally { _refreshingUsage = false; }
        Notify();
    }
    private void OnUsageTick(object? sender, EventArgs e)
    {
        Notify();
        Observe(SuspendIfIdleAsync);
    }
    private void OnUsagePollTick(object? sender, EventArgs e)
    {
        _usagePollTimer?.Stop();
        if (SignedIn && Connected) Observe(RefreshUsageAsync);
    }
    private static long? ResetCount(JsonElement data) => data.Get("rateLimitResetCredits").Get("availableCount").Number() is { } count && count >= 0 ? count : null;
    private static string WindowUsage(JsonElement window)
    {
        double? used = window.Get("usedPercent") is { ValueKind: JsonValueKind.Number } percent && percent.TryGetDouble(out var value) && double.IsFinite(value) ? value : null;
        var reset = window.Get("resetsAt").Number();
        if (used is null) return "— · —";
        var remaining = Math.Clamp(100 - used.Value, 0, 100);
        if (reset is null) return remaining.ToString("0.#") + "% · —";
        var duration = DateTimeOffset.FromUnixTimeSeconds(reset.Value) - DateTimeOffset.Now;
        var countdown = duration <= TimeSpan.Zero ? "reset due" : duration.TotalDays >= 1 ? $"{(int)duration.TotalDays}d {duration.Hours}h" : $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return remaining.ToString("0.#") + "% · " + countdown;
    }

    private static object Config(ProjectWorkspace workspace, string approvalMode, bool fastMode)
    {
        var config = new Dictionary<string, object>
        {
            ["approval_policy"] = "on-request", ["approvals_reviewer"] = approvalMode == "Auto approval" ? "auto_review" : "user",
            ["sandbox_workspace_write.writable_roots"] = workspace.Folders, ["sandbox_workspace_write.network_access"] = false
        };
        if (fastMode) config["features.fast_mode"] = true;
        return config;
    }

    public async Task<ConversationState> LoadChatAsync(ProjectState project, ChatState chat, bool earlier = false)
    {
        controller.EnsureProjectAvailable(ProfileId, project.Id);
        var state = Conversation(chat);
        if (chat.CodexThreadId is null) { state.Loaded = true; Notify(); return state; }
        if (state.Loading || state.Busy || state.Uncertain || state.Loaded && !earlier) return state;
        state.Loading = true; Notify();
        try
        {
            await ConnectAsync();
            JsonElement page;
            if (!state.UsesTurnHistory)
            {
                try
                {
                    page = await LoadItemHistoryAsync(chat, state, earlier);
                }
                catch (CodexRpcException error) when (error.Code == -32601 || error.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("does not support", StringComparison.OrdinalIgnoreCase))
                { state.UsesTurnHistory = true; page = await LoadTurnHistoryAsync(chat, state, false); }
            }
            else page = await LoadTurnHistoryAsync(chat, state, earlier);
            state.HistoryCursor = page.Text("nextCursor"); state.Loaded = true;
        }
        finally { state.Loading = false; Notify(); }
        return state;
    }

    private async Task<JsonElement> LoadItemHistoryAsync(ChatState chat, ConversationState state, bool earlier)
    {
        var previousFirstId = state.Messages.FirstOrDefault()?.Id;
        var cursor = earlier ? state.HistoryCursor : null;
        var cursors = new HashSet<string>();
        if (cursor is not null) cursors.Add(cursor);
        var page = await HistoryPageAsync("thread/items/list", chat.CodexThreadId, cursor, 100);
        while (true)
        {
            foreach (var entry in page.Get("data").Items()) RenderItem(state, entry.Get("item"), prepend: true, turnId: entry.Text("turnId"));
            cursor = page.Text("nextCursor");
            var first = state.Messages.FirstOrDefault();
            if (first is { Role: "You" } && first.Id != previousFirstId || cursor is null) break;
            if (!cursors.Add(cursor)) throw new IOException("Codex repeated a history cursor. Refresh chat before loading more history.");
            page = await HistoryPageAsync("thread/items/list", chat.CodexThreadId, cursor, 100);
            var items = page.Get("data").Items().ToArray();
            var userIndex = Array.FindIndex(items, entry => entry.Get("item").Text("type") == "userMessage");
            // Re-read only through the nearest older user message so the server supplies
            // the correct cursor for the next batch, without skipping the rest of this page.
            if (userIndex >= 0 && userIndex + 1 < items.Length)
                page = await HistoryPageAsync("thread/items/list", chat.CodexThreadId, cursor, userIndex + 1);
        }
        await LoadMessageMetadataAsync(state);
        return page;
    }

    private async Task<JsonElement> LoadTurnHistoryAsync(ChatState chat, ConversationState state, bool earlier)
    {
        var previousFirstId = state.Messages.FirstOrDefault()?.Id;
        var cursor = earlier ? state.HistoryCursor : null;
        var cursors = new HashSet<string>();
        JsonElement page;
        do
        {
            if (cursor is not null && !cursors.Add(cursor)) throw new IOException("Codex repeated a history cursor. Refresh chat before loading more history.");
            page = await HistoryPageAsync("thread/turns/list", chat.CodexThreadId, cursor, 25);
            foreach (var turn in page.Get("data").Items())
            {
                var turnId = turn.Text("id");
                if (turnId is not null)
                {
                    var firstId = "";
                    foreach (var item in turn.Get("items").Items())
                    {
                        if (item.Text("type") == "enteredReviewMode") break;
                        if (item.Text("type") == "userMessage") { firstId = item.Text("id") ?? ""; break; }
                    }
                    RememberFirstUser(state, turnId, firstId);
                }
                foreach (var item in turn.Get("items").Items().Reverse()) RenderItem(state, item, prepend: true, turnId: turnId);
            }
            cursor = page.Text("nextCursor");
        } while (cursor is not null && (state.Messages.FirstOrDefault() is not { Role: "You" } first || first.Id == previousFirstId));
        return page;
    }

    private async Task<JsonElement> HistoryPageAsync(string method, string? threadId, string? cursor, int limit)
    {
        while (true)
        {
            try
            {
                return await Client.CallAsync(method, new { threadId, limit, sortDirection = "desc", itemsView = method == "thread/turns/list" ? "full" : null, cursor });
            }
            catch (CodexPayloadTooLargeException) when (limit > 1) { limit = Math.Max(1, limit / 2); }
        }
    }

    public bool HasMoreChats(Guid projectId, bool archived = false) => _listCursors.TryGetValue((projectId, archived), out var page) && page.Cursor is not null;
    public async Task<JsonElement> ListSessionsAsync(bool archived, string? cursor)
    {
        if (!Connected) await ConnectAsync();
        return await Client.CallAsync("thread/list", new { limit = 100, cursor, archived, modelProviders = Array.Empty<string>(), useStateDbOnly = false, sourceKinds = new[] { "cli", "vscode", "appServer", "exec", "unknown" }, sortKey = "recency_at" });
    }
    public Task<JsonElement> ListProjectsAsync(string? cursor) => Client.CallAsync("project/list", new { cursor, limit = 100, sortKey = "position", sortDirection = "asc" });
    public async Task ImportChatsAsync(ProjectState project, bool more = false, bool archived = false)
    {
        if (!Connected) await ConnectAsync();
        await _historyLists.WaitAsync();
        try
        {
            var scope = string.Join("\n", project.Folders.OrderBy(folder => folder, FolderPaths.Comparer));
            var key = (project.Id, archived);
            var cursor = more && _listCursors.TryGetValue(key, out var previous) && previous.Scope == scope ? previous.Cursor : null;
            var page = await Client.CallAsync("thread/list", new { limit = 50, cursor, cwd = project.Folders.ToArray(), archived, sourceKinds = new[] { "cli", "vscode", "appServer", "exec", "unknown" }, sortKey = "updated_at" });
            var currentProject = AppController.Project(controller.State, ProfileId, project.Id);
            if (!project.Folders.SequenceEqual(currentProject.Folders, FolderPaths.Comparer)) return;
            var incoming = page.Get("data").Items().ToArray();
            if (incoming.Length > 0) controller.Commit(next =>
            {
                var target = AppController.Project(next, ProfileId, project.Id);
                foreach (var item in incoming)
                {
                    var id = item.Text("id"); if (id is null) continue;
                    var chat = target.Chats.FirstOrDefault(entry => entry.CodexThreadId == id);
                    if (chat is null) { chat = new ChatState { CodexThreadId = id }; target.Chats.Add(chat); }
                    if (!chat.HasCustomName)
                    {
                        chat.Name = Wire.Short(item.Text("name") ?? item.Text("preview") ?? "Codex chat", 100);
                        if (string.IsNullOrWhiteSpace(chat.Name)) chat.Name = "Codex chat";
                    }
                    chat.IsArchived = archived;
                    if (item.Get("createdAt").Number() is { } timestamp && timestamp >= 0 && timestamp <= 253402300799) chat.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                }
            });
            _listCursors[key] = (scope, page.Text("nextCursor"));
        }
        finally { _historyLists.Release(); Notify(); }
    }

    public async Task SendAsync(ProjectState project, ChatState chat, string text, CodexModel model, string effort, string approvalMode, IReadOnlyList<ComposerReference>? references = null, IReadOnlyList<ChatAttachment>? attachments = null, bool fastMode = false, bool continuingConflict = false, MessageEdit? replacement = null)
    {
        RequireNoExtensionInstall();
        var sentAt = DateTimeOffset.UtcNow;
        if (!Connected) await ConnectAsync();
        Touch();
        if (!SignedIn) throw new InvalidOperationException("Sign in to this account first.");
        if (chat.IsArchived) throw new InvalidOperationException("Restore this chat before sending.");
        var state = Conversation(chat);
        RequireConflictResolved(state, continuingConflict);
        if (state.Busy || state.Uncertain || state.Loading || IsSessionArchiving(chat.Id, chat.CodexThreadId) || IsSessionDeleting(chat.Id, chat.CodexThreadId)) throw new InvalidOperationException("This chat is busy, loading, or being archived or deleted. Wait or refresh its state before sending again.");
        var workspace = controller.WorkspaceForChat(ProfileId, project.Id, chat.Id);
        foreach (var folder in workspace.Folders) _ = FolderPaths.Canonicalize(folder);
        var input = BuildInput(text, references ?? [], workspace.Folders, attachments ?? []);
        if (state.ThreadId is not null && !state.Loaded) await LoadChatAsync(project, chat);
        RequireNoExtensionInstall();
        RequireConflictResolved(state, continuingConflict);
        if (state.Busy || state.Uncertain || state.Loading || IsSessionArchiving(chat.Id, chat.CodexThreadId) || IsSessionDeleting(chat.Id, chat.CodexThreadId)) throw new InvalidOperationException("This chat is busy. Wait before sending again.");
        if (replacement is not null)
        {
            if (state.QueueDispatching || state.QueueEditing) throw new InvalidOperationException("Wait for queue submission or editing to finish before editing history.");
            if (text.Length > 128 * 1024) throw new InvalidOperationException("Edited messages, attachments, and annotations must fit within 131,072 text characters.");
            AttachmentStore.Validate(attachments ?? []);
            if (System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(input)) > 7 * 1024 * 1024)
                throw new InvalidOperationException("The edited message is too large. Shorten it before saving.");
            ValidateMessageEdit(state, replacement);
            PauseQueue(state, "Queue paused after editing conversation history. Resume when ready.");
        }
        BeginFileTracking(state, workspace.PrimaryDirectory);
        state.ActiveFolders = workspace.Folders;
        controller.MarkProjectTaskStarted(ProfileId, project.Id);
        state.TaskStartedAt = sentAt; state.Submitting = true; Notify();
        var turnSent = false;
        var historyEditSent = false;
        var newThread = state.ThreadId is null;
        try
        {
            var setup = new Dictionary<string, object?> { ["cwd"] = workspace.PrimaryDirectory, ["model"] = model.Id, ["approvalPolicy"] = "on-request", ["sandbox"] = "workspace-write", ["config"] = Config(workspace, approvalMode, fastMode), ["excludeTurns"] = true,
                ["developerInstructions"] = "Project folders available to this chat:\n" + string.Join("\n", workspace.Folders) + "\nPrimary working directory: " + workspace.PrimaryDirectory };
            if (state.ThreadId is null)
            {
                if (project.CodexProjectId is not null) setup["projectId"] = project.CodexProjectId;
                var result = await Client.CallAsync("thread/start", setup);
                state.ThreadId = result.Get("thread").Text("id") ?? throw new IOException("Codex did not return a thread ID.");
                SessionListChanged?.Invoke(state.ThreadId);
            }
            else
            {
                setup["threadId"] = state.ThreadId;
                var resumed = await Client.CallAsync("thread/resume", setup);
                if (resumed.Get("thread").Get("status").Text("type") == "active")
                { state.Uncertain = true; throw new InvalidOperationException("Codex reports this conversation as active. Stop using it in the other client, then Refresh chat before sending."); }
            }
            if (chat.CodexThreadId != state.ThreadId)
                controller.EditChat(ProfileId, project.Id, chat.Id, item =>
                {
                    item.CodexThreadId = state.ThreadId;
                    item.NativeProjectId = project.CodexProjectId;
                    if (!item.HasCustomName && item.Name == "New chat")
                    {
                        var body = MessagePayload.Parse(text).Body;
                        item.Name = Wire.Short(string.IsNullOrWhiteSpace(body) ? attachments?.FirstOrDefault()?.Name ?? "Codex chat" : body.ReplaceLineEndings(" "), 70);
                    }
                    else item.HasCustomName = true;
                });
            if (replacement is not null) await RevertEditedMessageAsync(state, replacement, () => historyEditSent = true);
            state.Loaded = true;
            state.Terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            turnSent = true;
            var response = await Client.CallAsync("turn/start", new
            {
                threadId = state.ThreadId, input,
                cwd = workspace.PrimaryDirectory, model = model.Id, effort = model.Efforts.Count > 0 ? effort.ToLowerInvariant() : null, approvalPolicy = "on-request",
                serviceTierForTurn = fastMode ? "fast" : "default",
                sandboxPolicy = new { type = "workspaceWrite", writableRoots = workspace.Folders, networkAccess = false, excludeTmpdirEnvVar = true, excludeSlashTmp = true }
            });
            if (!state.Terminal.Task.IsCompleted) state.TurnId = response.Get("turn").Text("id");
            if (newThread) Observe(() => NameNewChatAsync(chat.Id, state.ThreadId!, text, model, attachments ?? [], fastMode));
        }
        catch (Exception error)
        {
            PauseQueue(state, "Queue paused because message submission did not complete: " + error.Message);
            if ((turnSent || historyEditSent) && error is not CodexRpcException) { state.Uncertain = true; state.Info("The request outcome is unknown. It was not retried. Reconnect and refresh this chat before sending again."); }
            else { state.LastTurnStatus = "failed"; state.TaskFinishedAt = DateTimeOffset.UtcNow; state.ClearActivities(); state.Terminal.TrySetResult(); }
            throw;
        }
        finally { state.Submitting = false; Notify(); if (!state.Busy && !state.Uncertain && !state.QueuePaused && state.Queue.Count > 0) Observe(() => DrainQueueAsync(state)); }
    }

    public async Task StopAsync(ConversationState state)
    {
        PauseQueue(state, "Queue paused by Stop.");
        if (state.Uncertain) throw new IOException("Task outcome is unknown after a lost connection. Reconnect and refresh the chat to reconcile its state.");
        if (!state.Busy) return;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (state.Busy && state.TurnId is null && !state.Uncertain && DateTimeOffset.UtcNow < deadline) await Task.Delay(100);
        if (state.Uncertain) throw new IOException("The connection was lost. Use Reconnect to recover or close the window to explicitly stop its process.");
        if (!state.Busy) return;
        if (state.TurnId is null) throw new InvalidOperationException("Codex has not acknowledged the task yet. Keep this window open and retry Stop after acknowledgement.");
        await Client.CallAsync("turn/interrupt", new { threadId = state.ThreadId, turnId = state.TurnId }, true);
        await state.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    public async Task StopAllAsync()
    {
        RequireNoExtensionInstall();
        CancelChatTitles();
        var predictionsStopped = CancelPredictionsAsync();
        PauseAllQueues("Queue paused while closing or stopping this account.");
        foreach (var state in _conversations.Values.Where(chat => chat.Busy || chat.Uncertain).ToArray()) await StopAsync(state);
        await predictionsStopped;
        await Task.WhenAll(_projectChatOperations.Values.Select(operation => operation.Completion.Task)
            .Concat(_archivingSessions.Values.Select(operation => operation.Completion.Task))
            .Concat(_deletingSessions.Values.Select(operation => operation.Completion.Task)).ToArray());
    }

    public async Task CompactAsync(ProjectState project, ChatState chat, string approvalMode, bool fastMode)
    {
        RequireNoExtensionInstall();
        if (!SignedIn || !Connected) throw new InvalidOperationException("Connect and sign in before compacting context.");
        var state = Conversation(chat);
        RequireConflictResolved(state);
        if (chat.IsArchived || state.ThreadId is null || state.Busy || state.Uncertain || state.Loading)
            throw new InvalidOperationException("Open an idle, saved chat before compacting context.");
        var workspace = controller.WorkspaceForChat(ProfileId, project.Id, chat.Id);
        BeginFileTracking(state, workspace.PrimaryDirectory);
        state.ActiveFolders = workspace.Folders;
        controller.MarkProjectTaskStarted(ProfileId, project.Id);
        state.TaskStartedAt = DateTimeOffset.UtcNow; state.Submitting = true; Notify();
        var compactSent = false;
        try
        {
            var resumed = await Client.CallAsync("thread/resume", new
            {
                threadId = state.ThreadId, cwd = workspace.PrimaryDirectory, excludeTurns = true,
                approvalPolicy = "on-request", sandbox = "workspace-write", config = Config(workspace, approvalMode, fastMode)
            });
            if (resumed.Get("thread").Get("status").Text("type") == "active")
            {
                state.Uncertain = true;
                throw new InvalidOperationException("This conversation is active in another client. Stop using it there, then Refresh chat before compacting.");
            }
            state.Compacting = true;
            state.Terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            compactSent = true; Notify();
            await Client.CallAsync("thread/compact/start", new { threadId = state.ThreadId });
        }
        catch (Exception error)
        {
            PauseQueue(state, "Queue paused because context compaction did not complete: " + error.Message);
            if (compactSent && error is not CodexRpcException)
            { state.Uncertain = true; state.Info("The compaction outcome is unknown. Reconnect and refresh this chat before retrying."); }
            else { state.Compacting = false; state.LastTurnStatus = "failed"; state.TaskFinishedAt = DateTimeOffset.UtcNow; state.ClearActivities(); state.Terminal.TrySetResult(); }
            throw;
        }
        finally { state.Submitting = false; Notify(); if (!state.Busy && !state.Uncertain && !state.QueuePaused && state.Queue.Count > 0) Observe(() => DrainQueueAsync(state)); }
    }

    public async Task ReconcileAsync(ProjectState project, ChatState chat)
    {
        await ConnectAsync(); var state = Conversation(chat);
        if (state.ThreadId is null) return;
        var response = await Client.CallAsync("thread/read", new { threadId = state.ThreadId });
        var status = response.Get("thread").Get("status");
        if (status.Text("type") is not ("idle" or "notLoaded")) throw new InvalidOperationException("Codex has not confirmed that this chat is idle. Keep it open and retry Refresh chat later.");
        state.Uncertain = false; state.Submitting = false; state.Compacting = false; state.ContextCompactionItemId = null; state.Terminal.TrySetResult(); state.TurnId = null; state.Loaded = false;
        state.ClearActivities();
        state.EditedFiles.Clear();
        state.Messages.Clear(); state.Transcript.Clear(); state.FirstUserMessageIds.Clear(); await LoadChatAsync(project, chat); Notify();
    }

    private void OnDisconnected(string reason)
    {
        CancelChatTitles();
        _ = CancelPredictionsAsync();
        PauseAllQueues("Queue paused after connection loss.");
        _initialized = false;
        _usageTimer?.Stop();
        SignedIn = false; Status = "Connection lost"; Notice = reason; _loginId = null; _loginUrl = null; LoginHint = "";
        foreach (var approval in Approvals) CloseQuestions(approval);
        Approvals.Clear();
        foreach (var state in _conversations.Values.Where(chat => chat.Busy)) { state.Uncertain = true; state.ContextCompactionItemId = null; state.ClearActivities(); state.Terminal.TrySetResult(); foreach (var item in state.Messages) item.IsStreaming = false; state.Info(reason); }
        Notify();
    }

    private void Observe(Func<Task> action)
    {
        async Task Run() { try { await action(); } catch (Exception error) { Report(error); } }
        _ = Run();
    }
    private void OnMessage(CodexClient connection, WireMessage message)
    {
        if (HandlePredictionMessage(connection, message)) return;
        if (HandleChatTitleMessage(connection, message)) return;
        var p = message.Parameters;
        if (message.Id.ValueKind != JsonValueKind.Undefined)
        {
            if (message.Method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "item/permissions/requestApproval" or "item/tool/requestUserInput" or "tool/requestUserInput")
            {
                if (Approvals.Count < 32)
                {
                    var approval = new PendingApproval(connection, message.Id, message.Method, p);
                    Approvals.Add(approval); RecordQuestions(approval, "Awaiting your reply");
                    ApprovalRequested?.Invoke(approval);
                }
                else Observe(() => connection.RejectAsync(message.Id, -32000, "Mini Codex reached its limit of 32 pending user requests. The action was not approved."));
            }
            else if (message.Method == "mcpServer/elicitation/request") Observe(() => connection.RespondAsync(message.Id, new { action = "decline" }));
            else Observe(() => connection.RejectAsync(message.Id));
            Notify(); return;
        }
        var state = Find(p.Text("threadId"));
        switch (message.Method)
        {
            case "mini/outputTooLarge":
                Notice = p.Text("message") ?? "An oversized Codex output was skipped.";
                state?.Info(Notice);
                if (state is not null && p.Text("method") == "turn/completed")
                {
                    state.Uncertain = true; state.ClearActivities(); state.Terminal.TrySetResult();
                    PauseQueue(state, "Refresh this chat's state before continuing; its completion message was too large.");
                }
                break;
            case "skills/changed": CapabilitiesChanged?.Invoke(this, EventArgs.Empty); break;
            case "account/login/completed":
                Touch();
                if (_loginId is not null && p.Text("loginId") == _loginId)
                {
                    _loginId = null; _loginUrl = null; LoginHint = "";
                    if (p.Get("success").ValueKind == JsonValueKind.True) Observe(RefreshAccountAsync);
                    else Notice = p.Text("error") ?? "Sign-in did not complete.";
                }
                break;
            case "account/updated": if (Connected) Observe(RefreshAccountAsync); break;
            case "configWarning": Notice = p.Text("summary") ?? "Codex reported a configuration warning."; break;
            case "warning": Notice = p.Text("message") ?? "Codex reported a warning."; break;
            case "windowsSandbox/setupCompleted": Notice = p.Get("success").ValueKind == JsonValueKind.True ? "Windows sandbox setup completed." : p.Text("error") ?? "Windows sandbox setup failed."; break;
            case "account/rateLimits/updated":
                ApplyUsage(p, partial: true);
                break;
            case "serverRequest/resolved":
                foreach (var approval in Approvals.Where(item => item.Key == p.Get("requestId").ToString()).ToArray()) { CloseQuestions(approval); Approvals.Remove(approval); }
                break;
            case "turn/started" when state is not null:
                if (!state.Busy) { state.TaskStartedAt = DateTimeOffset.UtcNow; BeginFileTracking(state, state.ActiveDirectory); }
                state.TaskStartedAt ??= DateTimeOffset.UtcNow;
                if (state.TurnId != p.Get("turn").Text("id")) { state.ContextCompactionItemId = null; state.ClearActivities(); }
                state.TurnId = p.Get("turn").Text("id");
                if (state.Terminal.Task.IsCompleted) state.Terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                break;
            case "turn/completed" when state is not null:
                Touch();
                state.LastTurnStatus = p.Get("turn").Text("status");
                state.TaskFinishedAt = DateTimeOffset.UtcNow; state.ClearActivities();
                state.EditedFiles.Clear();
                state.TurnId = null; state.Terminal.TrySetResult();
                foreach (var item in state.Messages) item.IsStreaming = false;
                if (!state.Compacting)
                    foreach (var activity in state.Transcript.Where(entry => entry.IsActivity)) activity.Expanded = false;
                state.Info(p.Get("turn").Text("status") == "failed" ? p.Get("turn").Get("error").Text("message") ?? "Task failed." : (state.Compacting ? "Context compaction " : "Task ") + p.Get("turn").Text("status") + ".");
                if (!state.Compacting && state.ConflictPause is null && p.Get("turn").Text("status") == "completed" && !_viewedConversations.ContainsValue(state.LocalId))
                    SetCompletionUnread(state.LocalId, true);
                state.Compacting = false;
                state.ContextCompactionItemId = null;
                state.Reviewing = false;
                if (p.Get("turn").Text("status") != "completed") PauseQueue(state, "Queue paused after " + p.Get("turn").Text("status") + ".");
                TaskCompleted?.Invoke(state, p.Get("turn").Text("status") ?? "unknown");
                foreach (var approval in Approvals.Where(item => item.ThreadId == state.ThreadId).ToArray()) { CloseQuestions(approval); Approvals.Remove(approval); }
                SessionListChanged?.Invoke(null);
                Observe(RefreshUsageAsync); break;
            case "item/started" or "item/completed" when state is not null:
                if (p.Get("item").Text("type") == "contextCompaction")
                {
                    var compactionTurnId = p.Text("turnId");
                    if (state.Busy && (compactionTurnId is null || state.TurnId is null || state.TurnId == compactionTurnId))
                    {
                        var compactionItemId = p.Get("item").Text("id");
                        if (message.Method == "item/started") state.ContextCompactionItemId = compactionItemId;
                        else if (state.ContextCompactionItemId == compactionItemId) state.ContextCompactionItemId = null;
                    }
                    break;
                }
                TrackTaskActivity(state, p.Get("item"), p.Text("turnId"), completed: message.Method == "item/completed");
                TrackFileChanges(state, p.Get("item"), p.Text("turnId"));
                RenderItem(state, p.Get("item"), streaming: message.Method == "item/started", turnId: p.Text("turnId") ?? state.TurnId); break;
            case "item/agentMessage/delta" when state is not null:
                if (IsCurrentTurn(state, p.Text("turnId"))) state.BeginActivity(p.Text("itemId") ?? "reply", ConversationActivityKind.Response);
                state.Put(p.Text("itemId") ?? "reply", "Codex", p.Text("delta") ?? "", delta: true, streaming: true); break;
            case "item/commandExecution/outputDelta" when state is not null: state.Put(p.Text("itemId") ?? "command", "Command", p.Text("delta") ?? "", delta: true); break;
            case "thread/tokenUsage/updated" when state is not null:
                var usage = p.Get("tokenUsage"); var window = usage.Get("modelContextWindow").Number(); var used = usage.Get("last").Get("totalTokens").Number();
                state.ContextUsed = used is >= 0 ? used : null;
                state.ContextCapacity = window is > 0 ? window : null;
                break;
            case "error" when state is not null: state.Info(p.Get("error").Text("message") ?? "Codex reported an error."); break;
        }
        Notify();
        if (message.Method == "turn/completed" && state is not null && p.Get("turn").Text("status") == "completed") Observe(() => DrainQueueAsync(state));
    }

    private static void RenderItem(ConversationState state, JsonElement item, bool prepend = false, bool streaming = false, string? turnId = null)
    {
        var id = item.Text("id"); if (id is null) return;
        var type = item.Text("type");
        switch (type)
        {
            case "agentMessage":
                state.Put(id, "Codex", item.Text("text") ?? "", prepend: prepend, streaming: streaming);
                if (item.Get("questions").Items().Any())
                {
                    var message = state.Messages.FirstOrDefault(message => message.Id == id);
                    if (message is not null && message.QuestionBlock is null) message.SetQuestions(QuestionTranscript.FromAsync(item.Get("questions"), id));
                    state.ReconcileQuestionReplies();
                }
                break;
            case "enteredReviewMode": state.Put(id, "Status", "Code review started: " + (item.Text("review") ?? ""), prepend: prepend); break;
            case "exitedReviewMode": state.Put(id, "Codex", item.Text("review") ?? "Code review completed.", prepend: prepend); break;
            case "userMessage":
                var content = item.Get("content").Items().ToArray();
                state.Put(id, "You", UserMessageText(item), prepend: prepend);
                var userMessage = state.Messages.First(message => message.Id == id);
                userMessage.InputReferences = content.Where(part => (part.Text("type") is "skill" or "mention") && part.Text("path") is not null && part.Text("name") is not null)
                    .Select(part => new ComposerReference(part.Text("type") == "skill" ? ReferenceKind.Skill : ReferenceKind.Connector, part.Text("name")!, part.Text("path")!)).ToArray();
                if (turnId is not null)
                {
                    if (!prepend && !state.FirstUserMessageIds.ContainsKey(turnId))
                        RememberFirstUser(state, turnId, state.Reviewing ? "" : state.Messages.FirstOrDefault(message => message.Role == "You" && message.TurnId == turnId)?.Id ?? id);
                    userMessage.SetTurnInput(turnId, state.FirstUserMessageIds.TryGetValue(turnId, out var firstUser) ? id != firstUser : null);
                }
                state.ReconcileQuestionReplies();
                break;
            case "commandExecution": state.Put(id, "Command", (item.Text("command") ?? "") + "\n" + (item.Text("aggregatedOutput") ?? item.Text("status") ?? ""), prepend: prepend); break;
            case "fileChange": state.Put(id, "Files", string.Join("\n", item.Get("changes").Items().Select(change => change.Text("path"))), prepend: prepend); break;
            case "webSearch": state.Put(id, "Search", item.Text("query") ?? "Web search", prepend: prepend); break;
            case "imageGeneration":
                var generated = GeneratedImageStore.Resolve(item.Text("savedPath"), item.Text("result"));
                var failure = item.Get("failure").Text("type");
                var imageFailed = failure is not null || item.Text("status") == "failed";
                var imageStatus = failure == "usageLimitExceeded" ? "Image generation failed: usage limit reached."
                    : imageFailed ? "Image generation failed."
                    : item.Text("status") is "canceled" or "cancelled" or "interrupted" ? "Image generation stopped."
                    : item.Text("status") == "completed" ? "Image generated, but no local preview is available." : "Generating image…";
                var imageText = generated is null ? imageStatus : (imageFailed ? imageStatus + "\n\n" : "") + GeneratedImageStore.Markdown(generated);
                state.Put(id, "Codex", imageText, prepend: prepend);
                break;
            case "mcpToolCall":
                RenderOutputImages(state, id, item.Get("result").Get("content"), prepend);
                break;
            case "dynamicToolCall" when item.Text("tool") is "request_user_input" or "request_user_input_async":
                var arguments = item.Get("arguments");
                if (arguments.ValueKind == JsonValueKind.String)
                {
                    try { using var document = JsonDocument.Parse(arguments.GetString()!); arguments = document.RootElement.Clone(); }
                    catch (JsonException) { break; }
                }
                if (!arguments.Get("questions").Items().Any()) break;
                var questionId = "questions/" + id;
                var old = state.Messages.FirstOrDefault(message => message.Id == questionId)?.QuestionBlock;
                var output = QuestionTranscript.OutputAnswers(item.Get("contentItems"));
                if (output.ValueKind == JsonValueKind.Object)
                    state.Put(questionId, "Questions", QuestionTranscript.From(arguments.Get("questions"), output, "Reply sent").Serialize(), prepend: prepend);
                else if (old is null)
                    state.Put(questionId, "Questions", QuestionTranscript.From(arguments.Get("questions"), default, "Recorded question").Serialize(), prepend: prepend);
                break;
            case "dynamicToolCall":
                RenderOutputImages(state, id, item.Get("contentItems"), prepend);
                break;
        }
    }

    private static void RenderOutputImages(ConversationState state, string id, JsonElement content, bool prepend)
    {
        var parts = content.Items().Where(part => part.Text("type") is "image" or "inputImage").Take(20).ToArray();
        var images = parts
            .Select(part => GeneratedImageStore.Resolve(null, part.Text("type") == "image" ? part.Text("data") : part.Text("imageUrl")))
            .OfType<string>().Distinct(FolderPaths.Comparer).ToArray();
        if (images.Length > 0) state.Put(id, "Codex", string.Join("\n\n", images.Select(GeneratedImageStore.Markdown)), prepend: prepend);
        else if (parts.Length > 0) state.Put(id, "Codex", "Image preview unavailable.", prepend: prepend);
    }

    public async Task ResolveApprovalAsync(PendingApproval approval, bool accept, bool session, Dictionary<string, object>? answers = null)
    {
        if (!_resolvingApprovals.Add(approval.Key)) throw new InvalidOperationException("This request is already being answered.");
        try
        {
        Touch();
        if (!ReferenceEquals(approval.Connection, _client) || !Approvals.Contains(approval)) throw new InvalidOperationException("This approval is no longer pending.");
        if (approval.Method.Contains("requestUserInput", StringComparison.Ordinal))
        {
            await approval.Connection.RespondAsync(approval.Id, new { answers = answers ?? [] });
            RecordQuestions(approval, accept ? "Reply sent" : "Question skipped", answers);
        }
        else if (approval.Method.Contains("permissions", StringComparison.Ordinal))
            await approval.Connection.RespondAsync(approval.Id, new { permissions = accept ? (object)approval.Parameters.Get("permissions") : new { }, scope = "turn" });
        else
        {
            var choice = accept ? session ? "acceptForSession" : "accept" : "decline";
            var decisions = approval.Parameters.Get("availableDecisions").Items().Select(item => item.Text()).OfType<string>().ToArray();
            if (decisions.Length > 0 && !decisions.Contains(choice)) throw new InvalidOperationException("That decision is not available for this request.");
            await approval.Connection.RespondAsync(approval.Id, new { decision = choice });
        }
        Approvals.Remove(approval); Notify();
        }
        finally { _resolvingApprovals.Remove(approval.Key); }
    }
    private readonly HashSet<string> _resolvingApprovals = [];

    public async Task EditChatAsync(ProjectState project, ChatState chat, string action, string? name = null)
    {
        controller.EnsureProjectAvailable(ProfileId, project.Id);
        var state = Conversation(chat);
        if (state.Busy || state.Uncertain || state.QueueDispatching || state.QueueEditing || state.Steering) throw new InvalidOperationException("Finish pending submission or editing, then stop or reconcile the task before changing this chat.");
        if (IsSessionArchiving(chat.Id, chat.CodexThreadId)) throw new InvalidOperationException("Wait for this chat to finish archiving before changing it.");
        if (IsSessionDeleting(chat.Id, chat.CodexThreadId)) throw new InvalidOperationException("Wait for this chat to finish deleting before changing it.");
        if (action == "rename") { await RenameChatAsync(project.Id, chat.Id, name ?? throw new ArgumentNullException(nameof(name))); return; }
        var operation = action == "delete" || action == "archive" && !chat.IsArchived ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        var operations = action == "delete" ? _deletingSessions : _archivingSessions;
        if (operation is not null) operations.Add(chat.Id, (project.Id, chat.CodexThreadId, operation, false));
        try
        {
            if (operation is not null)
            {
                PauseQueue(state, action == "delete" ? "Queue paused because this chat is being deleted." : "Queue paused because this chat was archived.");
                Touch(); Notify();
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            if (chat.CodexThreadId is { } id)
            {
                await ConnectAsync();
                if (operation is not null) operations[chat.Id] = (project.Id, id, operation, true);
                var method = action switch { "archive" => chat.IsArchived ? "thread/unarchive" : "thread/archive", "delete" => "thread/delete", _ => null };
                if (method is not null) await Client.CallAsync(method, new { threadId = id, name });
            }
            switch (action)
            {
                case "archive": controller.EditChat(ProfileId, project.Id, chat.Id, item => item.IsArchived = !item.IsArchived); break;
                case "delete": controller.DeleteChat(ProfileId, project.Id, chat.Id); _conversations.Remove(chat.Id); break;
            }
        }
        finally
        {
            if (operation is not null) { operations.Remove(chat.Id); operation.TrySetResult(); Notify(); }
        }
    }

    public async Task<Guid> DuplicateChatAsync(ProjectState project, ChatState chat)
    {
        controller.EnsureProjectAvailable(ProfileId, project.Id);
        var state = Conversation(chat);
        if (state.Busy || state.Uncertain || state.Loading) throw new InvalidOperationException("Stop or reconcile this task and finish loading before duplicating it.");
        if (chat.CodexThreadId is null) return controller.AddChat(ProfileId, project.Id, chat);
        state.Loading = true; Notify();
        try
        {
            await ConnectAsync();
            var result = await Client.CallAsync("thread/fork", new { threadId = chat.CodexThreadId, excludeTurns = true });
            var remoteId = result.Get("thread").Text("id") ?? throw new IOException("Codex did not return the forked chat ID.");
            SessionListChanged?.Invoke(remoteId);
            var copy = new ChatState { Name = chat.Name + " (copy)", CodexThreadId = remoteId };
            controller.Commit(next =>
            {
                var destination = AppController.Project(next, ProfileId, project.Id);
                destination.IsArchived = false;
                destination.Chats.Add(copy);
            });
            return copy.Id;
        }
        finally { state.Loading = false; Notify(); }
    }

    public async Task SetupSandboxAsync()
    {
        if (HasWork) throw new InvalidOperationException("Stop this account's tasks before setting up the Windows sandbox.");
        await ConnectAsync();
        await Client.CallAsync("windowsSandbox/setupStart", new { mode = "elevated" }, true);
        Notice = "Windows sandbox setup started. Complete the Windows prompt if it appears."; Notify();
    }

    public async ValueTask DisposeAsync()
    {
        if (HasWork) throw new InvalidOperationException("Stop active work before closing this account.");
        await _connect.WaitAsync();
        try
        {
            if (_disposed) return;
            if (HasWork) throw new InvalidOperationException("Stop active work before closing this account.");
            if (LoginPending) await CancelLoginAsync();
            CancelChatTitles();
            var connection = _client; _client = null; _initialized = false;
            if (connection is not null) await connection.DisposeAsync();
            _disposed = true;
            if (_usageTimer is not null) { _usageTimer.Stop(); _usageTimer.Tick -= OnUsageTick; _usageTimer = null; }
            if (_usagePollTimer is not null) { _usagePollTimer.Stop(); _usagePollTimer.Tick -= OnUsagePollTick; _usagePollTimer = null; }
            Approvals.Clear(); Models.Clear(); _conversations.Clear(); SignedIn = false; Status = "Not connected"; Notify();
        }
        finally { _connect.Release(); }
    }
}
