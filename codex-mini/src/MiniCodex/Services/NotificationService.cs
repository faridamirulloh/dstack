using System.Text.Json;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
using MiniCodex.Codex;
using MiniCodex.Presentation;

namespace MiniCodex.Services;

public sealed class NotificationService : IDisposable
{
    private sealed record Entry(Guid Profile, Guid? Chat, AccountRuntime Runtime, PendingApproval? Request, DateTimeOffset Created)
    { public HashSet<string> Allowed { get; } = ["open"]; }
    private readonly AppController _controller;
    private readonly WindowManager _windows;
    private readonly Action<ToastNotification> _show;
    private readonly Func<Guid, Task> _openAccount;
    private readonly AccountUsageMonitor? _usageMonitor;
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly Dictionary<ConversationState, HashSet<string>> _taskNotifications = [];
    private readonly HashSet<string> _processing = [];
    private static bool _activationRegistered;
    private static string _activationError = "";
    public string Error { get; private set; } = "";

    public static void RegisterActivation()
    {
        if (_activationRegistered) return;
        _activationRegistered = true;
        try { ToastNotificationManagerCompat.OnActivated += args => Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (Application.Current is App { WindowManager: { } windows })
                await windows.Notifications.ActivateAsync(args.Argument, args.UserInput.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString() ?? ""));
        })); }
        catch (Exception error) { _activationError = error.Message; }
    }
    public NotificationService(AppController controller, WindowManager windows, Action<ToastNotification>? show = null, bool monitorAccounts = true, Func<Guid, Task>? openAccount = null)
    {
        _controller = controller; _windows = windows;
        _show = show ?? (notification => ToastNotificationManagerCompat.CreateToastNotifier().Show(notification));
        _openAccount = openAccount ?? windows.FocusAccountAsync;
        windows.Runtimes.TaskCompleted += Completed;
        windows.Runtimes.ApprovalRequested += Requested;
        windows.Runtimes.UsageLimitsReset += LimitsReset;
        windows.Runtimes.UsageLimitsWarning += LimitsWarning;
        windows.Runtimes.Updated += Cleanup;
        if (monitorAccounts) { _usageMonitor = new(controller, windows.Runtimes, windows.HasAccountWindow); _usageMonitor.Start(); }
    }
    private (Guid Chat, string Name)? ChatFor(AccountRuntime runtime, string? threadId, Guid? chatId = null)
    {
        var chat = _controller.State.Profiles.FirstOrDefault(profile => profile.Id == runtime.ProfileId)?.Projects.SelectMany(project => project.Chats)
            .FirstOrDefault(chat => chatId is { } id ? chat.Id == id : threadId is not null && chat.CodexThreadId == threadId);
        return chat is null ? null : (chat.Id, chat.Name);
    }
    private void Completed(AccountRuntime runtime, ConversationState state, string status)
    {
        if (state.ConflictPause is not null || ChatFor(runtime, state.ThreadId, state.LocalId) is not { } chat) return;
        if (status == "completed" && runtime.TaskActivity(state) is ("Waiting for your answer" or "Waiting for approval")) return;
        var activity = status == "interrupted" ? "Stopped" : status == "failed" ? "Failed" : "Completed";
        if ((status is "failed" or "interrupted") && !TaskNotifications(state).Add(state.TaskRunId.ToString("N") + "/" + activity)) return;
        var entry = new Entry(runtime.ProfileId, chat.Chat, runtime, null, DateTimeOffset.UtcNow);
        Publish(entry, status == "interrupted" ? "Task stopped" : "Task " + status, chat.Name + (state.Queue.Count > 0 ? $" · {state.Queue.Count} queued" : ""), null);
    }
    private void Requested(AccountRuntime runtime, PendingApproval request)
    {
        if (ChatFor(runtime, request.ThreadId) is not { } chat) return;
        var entry = new Entry(runtime.ProfileId, chat.Chat, runtime, request, DateTimeOffset.UtcNow);
        Publish(entry, request.Title, chat.Name, request);
    }
    private void LimitsReset(AccountRuntime runtime, UsageLimitReset reset)
    {
        Publish(new Entry(runtime.ProfileId, null, runtime, null, DateTimeOffset.UtcNow), "Usage limits reset", reset.Description, null);
    }
    private void LimitsWarning(AccountRuntime runtime, UsageLimitWarning warning)
    {
        Publish(new Entry(runtime.ProfileId, null, runtime, null, DateTimeOffset.UtcNow), warning.Exhausted ? "Usage limit exhausted" : "Usage limit low", warning.Description, null);
    }
    private void Publish(Entry entry, string title, string body, PendingApproval? request)
    {
        try
        {
            if (_activationError.Length > 0) throw new IOException(_activationError);
            while (_entries.Count >= 128) Remove(_entries.OrderBy(pair => pair.Value.Request is not null).ThenBy(pair => pair.Value.Created).First().Key);
            var token = Guid.NewGuid().ToString("N"); _entries[token] = entry;
            var open = Arguments(entry, token, "open");
            var actions = new ToastActionsCustom();
            actions.Buttons.Add(new ToastButton(entry.Chat is null ? "Open account" : "Open chat", open));
            if (request is not null && request.Method.Contains("requestUserInput", StringComparison.Ordinal))
            {
                var questions = request.Parameters.Get("questions").Items().ToArray();
                if (questions.Length == 1 && questions[0].Get("isSecret").ValueKind != JsonValueKind.True)
                {
                    var question = questions[0];
                    var options = question.Get("options").Items().ToArray();
                    var labels = options.Select(option => (option.Text("label") ?? "") + (option.Text("description") is { Length: > 0 } detail ? " · " + detail : "")).ToArray();
                    if (options.Length <= 5 && labels.All(label => label.Length <= 150) && (question.Text("question")?.Length ?? 0) <= 240)
                    {
                        body = question.Text("question") ?? body;
                        if (options.Length > 0)
                        {
                            var selection = new ToastSelectionBox("answer") { Title = "Choose an answer" };
                            for (var index = 0; index < labels.Length; index++) selection.Items.Add(new ToastSelectionBoxItem(index.ToString(), labels[index]));
                            actions.Inputs.Add(selection);
                        }
                        actions.Inputs.Add(new ToastTextBox("custom") { PlaceholderContent = "Custom answer (overrides selection)" });
                        actions.Buttons.Add(new ToastButton("Submit answer", Arguments(entry, token, "answer")));
                        entry.Allowed.Add("answer");
                    }
                }
            }
            else if (request is not null)
            {
                var scope = request.Parameters.EnumerateObject().Where(property => property.Name is not ("threadId" or "turnId" or "itemId" or "availableDecisions"))
                    .ToDictionary(property => property.Name, property => property.Value);
                var details = JsonSerializer.Serialize(scope);
                var decisions = request.Parameters.Get("availableDecisions").Items().ToArray();
                if (details.Length <= 450 && decisions.All(decision => decision.ValueKind == JsonValueKind.String)
                    && (decisions.Length == 0 || decisions.Any(decision => decision.GetString() == "accept")))
                {
                    body = details;
                    actions.Buttons.Add(new ToastButton("Allow once", Arguments(entry, token, "accept")));
                    entry.Allowed.Add("accept");
                }
                if (decisions.Length == 0 || decisions.Any(decision => decision.Text() == "decline"))
                { actions.Buttons.Add(new ToastButton("Deny", Arguments(entry, token, "decline"))); entry.Allowed.Add("decline"); }
            }
            var name = _controller.Profile(entry.Profile).Name;
            var content = new ToastContent
            {
                Launch = open,
                Visual = new ToastVisual { BindingGeneric = new ToastBindingGeneric { Children = { new AdaptiveText { Text = Wire.Short(name + " · " + title, 180) }, new AdaptiveText { Text = body } } } },
                Actions = actions
            };
            var notification = new ToastNotification(content.GetXml()) { Tag = token[..16], Group = "mini-codex", ExpirationTime = DateTimeOffset.Now.AddHours(request is null ? 1 : 6) };
            _show(notification); Error = "";
        }
        catch (Exception error)
        {
            Error = "Windows notification unavailable: " + Wire.Short(error.Message, 300);
            entry.Runtime.Report(new IOException(Error));
        }
    }
    private static string Arguments(Entry entry, string token, string action) => entry.Chat is { } chat
        ? $"profile={entry.Profile:D}&chat={chat:D}&token={token}&action={action}"
        : $"profile={entry.Profile:D}&target=account&token={token}&action={action}";
    public async Task ActivateAsync(string arguments, IReadOnlyDictionary<string, string> input)
    {
        try
        {
            if (arguments.Length > 2048) return;
            var values = arguments.Split('&').Select(part => part.Split('=', 2)).Where(part => part.Length == 2).ToDictionary(part => part[0], part => part[1]);
            if (!Guid.TryParse(values.GetValueOrDefault("profile"), out var profile)) return;
            var token = values.GetValueOrDefault("token") ?? ""; var action = values.GetValueOrDefault("action");
            if (values.GetValueOrDefault("target") == "account")
            {
                if (action != "open" || _entries.TryGetValue(token, out var accountEntry) && (accountEntry.Profile != profile || accountEntry.Chat is not null)) return;
                await _openAccount(profile); return;
            }
            if (!Guid.TryParse(values.GetValueOrDefault("chat"), out var chat)) return;
            if (!_entries.TryGetValue(token, out var entry) || entry.Profile != profile || entry.Chat != chat || entry.Request is not { } request)
            { await _windows.OpenChatAsync(profile, chat); return; }
            if (!entry.Runtime.Connected || !entry.Runtime.Approvals.Contains(request)) { Remove(token); await _windows.OpenChatAsync(profile, chat); return; }
            if (action is null || !entry.Allowed.Contains(action)) return;
            if (action == "open") { await _windows.OpenChatAsync(profile, chat, request.Key); return; }
            if (!_processing.Add(token)) return;
            try
            {
                entry.Runtime.Touch();
                if (action == "answer" && request.Method.Contains("requestUserInput", StringComparison.Ordinal))
                {
                    var questions = request.Parameters.Get("questions").Items().ToArray();
                    if (questions.Length != 1 || questions[0].Get("isSecret").ValueKind == JsonValueKind.True) return;
                    var question = questions[0]; var id = question.Text("id"); if (id is null) return;
                    var answer = input.GetValueOrDefault("custom")?.Trim();
                    if (string.IsNullOrEmpty(answer) && int.TryParse(input.GetValueOrDefault("answer"), out var index))
                    {
                        var options = question.Get("options").Items().ToArray();
                        if (index >= 0 && index < options.Length) answer = options[index].Text("label");
                    }
                    if (string.IsNullOrWhiteSpace(answer) || answer.Length > 16000) { await _windows.OpenChatAsync(profile, chat, request.Key); return; }
                    await entry.Runtime.ResolveApprovalAsync(request, true, false, new() { [id] = new { answers = new[] { answer } } });
                }
                else if ((action is "accept" or "decline") && !request.Method.Contains("requestUserInput", StringComparison.Ordinal))
                    await entry.Runtime.ResolveApprovalAsync(request, action == "accept", false);
                else return;
                Remove(token);
            }
            finally { _processing.Remove(token); }
        }
        catch (Exception error) { AppDialog.Show(error.Message, "Notification action could not complete"); }
    }
    private void Cleanup(object? sender, EventArgs e)
    {
        foreach (var (token, entry) in _entries.ToArray())
            if (DateTimeOffset.UtcNow - entry.Created > TimeSpan.FromHours(entry.Request is null ? 1 : 6)
                || entry.Request is { } request && (!entry.Runtime.Connected || !entry.Runtime.Approvals.Contains(request))) Remove(token);
        NotifyTaskStates();
    }
    private HashSet<string> TaskNotifications(ConversationState state)
    {
        if (!_taskNotifications.TryGetValue(state, out var sent)) _taskNotifications[state] = sent = [];
        return sent;
    }
    private void NotifyTaskStates()
    {
        var current = _windows.Runtimes.All.SelectMany(runtime => runtime.Conversations.Select(state => (Runtime: runtime, State: state))).ToArray();
        var keep = current.Select(item => item.State).ToHashSet();
        foreach (var removed in _taskNotifications.Keys.Where(state => !keep.Contains(state)).ToArray()) _taskNotifications.Remove(removed);
        foreach (var (runtime, state) in current)
        {
            var known = _taskNotifications.ContainsKey(state);
            var sent = TaskNotifications(state);
            var activity = runtime.TaskActivity(state);
            if (activity is "Paused" or "Stopped" or "Failed")
            {
                if (sent.Add(state.TaskRunId.ToString("N") + "/" + activity) && known && ChatFor(runtime, state.ThreadId, state.LocalId) is { } chat)
                    Publish(new Entry(runtime.ProfileId, chat.Chat, runtime, null, DateTimeOffset.UtcNow), "Task " + activity.ToLowerInvariant(),
                        chat.Name + (activity == "Paused" ? "\nPaused after a file conflict. Open chat to Continue or Cancel." : ""), null);
            }
            if (activity != "Waiting for your answer") continue;
            foreach (var message in state.Messages.Where(message => message.CanAnswer && message.QuestionBlock is { IsAsync: true }).ToArray())
                if (sent.Add("question/" + message.Id) && known && ChatFor(runtime, state.ThreadId, state.LocalId) is { } chat)
                    Publish(new Entry(runtime.ProfileId, chat.Chat, runtime, null, DateTimeOffset.UtcNow), activity, chat.Name, null);
        }
    }
    private void Remove(string token)
    {
        _entries.Remove(token);
        try { ToastNotificationManagerCompat.History.Remove(token[..16], "mini-codex"); } catch (Exception) { }
    }
    public void Dispose()
    {
        _usageMonitor?.Dispose();
        _windows.Runtimes.TaskCompleted -= Completed; _windows.Runtimes.ApprovalRequested -= Requested; _windows.Runtimes.UsageLimitsReset -= LimitsReset; _windows.Runtimes.Updated -= Cleanup;
        _windows.Runtimes.UsageLimitsWarning -= LimitsWarning;
        foreach (var token in _entries.Where(pair => pair.Value.Request is not null).Select(pair => pair.Key).ToArray()) Remove(token);
        _entries.Clear();
        _taskNotifications.Clear();
    }
}
