using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed partial class RuntimeRegistry(AppController controller, DraftStore drafts)
{
    private readonly Dictionary<Guid, AccountRuntime> _accounts = [];
    private readonly Dictionary<Guid, int> _borrowers = [];
    private readonly HashSet<Guid> _releaseRequested = [];
    private readonly Dictionary<Guid, Task> _releases = [];
    private readonly Dictionary<Guid, AccountAuthentication> _authentication = [];
    private readonly HashSet<Guid> _removing = [];
    public bool IsStopping { get; private set; }
    public event EventHandler? Updated;
    public event Action<AccountRuntime, ConversationState, string>? TaskCompleted;
    public event Action<AccountRuntime, PendingApproval>? ApprovalRequested;
    public event Action<AccountRuntime, UsageLimitReset>? UsageLimitsReset;
    public event Action<AccountRuntime, UsageLimitWarning>? UsageLimitsWarning;
    private readonly RateLimitResetTracker _resetTracker = new();
    private readonly Dictionary<Guid, UsageLimitReport> _limitReports = [];
    public UsageLimitReport? LimitReport(Guid id) => _limitReports.GetValueOrDefault(id);
    private readonly Dictionary<Guid, AccountSnapshot> _snapshots = [];
    public AccountSnapshot? Snapshot(Guid id) => _snapshots.GetValueOrDefault(id);
    public IEnumerable<AccountRuntime> All => _accounts.Values;
    public AccountRuntime Get(Guid id)
    {
        if (IsStopping) throw new InvalidOperationException("Mini Codex is exiting.");
        if (_removing.Contains(id)) throw new InvalidOperationException("This account is being removed.");
        if (!controller.State.Profiles.Any(profile => profile.Id == id)) throw new InvalidOperationException("This account has been removed.");
        if (_releases.ContainsKey(id)) throw new InvalidOperationException("This account is closing. Wait for it to finish.");
        _releaseRequested.Remove(id);
        if (_accounts.TryGetValue(id, out var existing)) return existing;
        var runtime = new AccountRuntime(controller, id, directory => ClaimStateDirectory(id, directory), drafts);
        runtime.ConflictIsBlocked = IsFileConflictBlocked;
        runtime.FilesEdited += (state, files) => OnFilesEdited(runtime, state, files);
        runtime.TaskCompleted += (state, status) => TaskCompleted?.Invoke(runtime, state, status);
        runtime.ApprovalRequested += approval => ApprovalRequested?.Invoke(runtime, approval);
        runtime.UsageObserved += report => UsageObserved(runtime, report);
        runtime.Updated += OnUpdated; _accounts.Add(id, runtime); return runtime;
    }
    public AccountRuntime? Find(Guid id) => _accounts.GetValueOrDefault(id);
    public AccountAuthentication? Authentication(Guid id) => _authentication.GetValueOrDefault(id);
    public void ForgetAuthentication(Guid id) { _authentication.Remove(id); _snapshots.Remove(id); _resetTracker.Forget(id); _limitReports.Remove(id); }
    public bool IsRemoving(Guid id) => _removing.Contains(id);
    public void BeginRemoval(Guid id)
    {
        if (!_removing.Add(id)) throw new InvalidOperationException("This account is already being removed.");
        Updated?.Invoke(this, EventArgs.Empty);
    }
    public void EndRemoval(Guid id, bool removed)
    {
        _removing.Remove(id);
        if (removed) { ForgetAuthentication(id); _releaseRequested.Remove(id); _borrowers.Remove(id); }
        Updated?.Invoke(this, EventArgs.Empty);
    }
    public async Task WaitForReleaseAsync(Guid id)
    { if (_releases.TryGetValue(id, out var release)) await release; }
    public async Task<RuntimeLease> BorrowAsync(Guid id)
    {
        await WaitForReleaseAsync(id);
        var runtime = Get(id);
        _borrowers[id] = _borrowers.GetValueOrDefault(id) + 1;
        return new RuntimeLease(this, id, runtime);
    }
    public sealed class RuntimeLease(RuntimeRegistry owner, Guid id, AccountRuntime runtime) : IAsyncDisposable
    {
        private bool _returned;
        public AccountRuntime Runtime { get; } = runtime;
        public async ValueTask DisposeAsync()
        {
            if (_returned) return;
            _returned = true;
            if (!ReferenceEquals(owner.Find(id), Runtime)) return;
            var remaining = owner._borrowers.GetValueOrDefault(id) - 1;
            if (remaining > 0) owner._borrowers[id] = remaining;
            else
            {
                owner._borrowers.Remove(id);
                if (owner._releaseRequested.Contains(id)) await owner.ReleaseAsync(id);
            }
        }
    }
    private void ClaimStateDirectory(Guid profileId, string directory)
    {
        foreach (var profile in controller.State.Profiles.Where(entry => entry.Id != profileId))
        {
            var otherHome = CodexInstallation.Home(profile);
            var otherState = Find(profile.Id)?.StateDirectory ?? otherHome;
            if (Directory.Exists(otherState)) otherState = FolderPaths.Canonicalize(otherState);
            if (FolderPaths.Comparer.Equals(directory, otherState))
                throw new IOException("Another account profile uses this Codex state directory. Profiles cannot share SQLite state.");
        }
    }
    private void OnUpdated(object? sender, EventArgs e)
    {
        RefreshFileConflictWaits();
        if (sender is AccountRuntime { Authentication: { } authentication } runtime)
        {
            _authentication[runtime.ProfileId] = authentication;
            if (!authentication.SignedIn) { _resetTracker.Forget(runtime.ProfileId); _limitReports.Remove(runtime.ProfileId); }
            if (runtime.Connected || runtime.Suspended) _snapshots[runtime.ProfileId] = new(runtime.Usage, runtime.Credits, runtime.TaskSummary, runtime.Status, runtime.UsageTooltip, runtime.AvailableResetCount);
        }
        Updated?.Invoke(this, e);
    }
    private void UsageObserved(AccountRuntime runtime, UsageLimitReport report)
    {
        if (!runtime.SignedIn || runtime.Authentication is not { SignedIn: true } || IsRemoving(runtime.ProfileId)) return;
        _limitReports[runtime.ProfileId] = report;
        if (_resetTracker.Observe(runtime.ProfileId, runtime.UsageScope, report, out var warning) is { } reset) UsageLimitsReset?.Invoke(runtime, reset);
        if (warning is not null) UsageLimitsWarning?.Invoke(runtime, warning);
    }
    public async Task ReleaseAsync(Guid id, bool force = false)
    {
        _releaseRequested.Add(id);
        if (!force && _borrowers.GetValueOrDefault(id) > 0) return;
        if (_releases.TryGetValue(id, out var pending)) { await pending; if (!force) return; }
        if (!_accounts.TryGetValue(id, out var runtime)) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _releases[id] = completion.Task;
        try
        {
            await runtime.DisposeAsync(); runtime.Updated -= OnUpdated; _accounts.Remove(id); _releaseRequested.Remove(id); _borrowers.Remove(id);
        }
        finally { _releases.Remove(id); completion.TrySetResult(); Updated?.Invoke(this, EventArgs.Empty); }
    }
    public async Task StopAndDisposeAsync(Action? beforeDispose = null)
    {
        IsStopping = true;
        try
        {
            foreach (var runtime in _accounts.Values.ToArray())
                if (!runtime.Connected && runtime.HasWork) await runtime.StopAfterLossAsync();
                else await runtime.StopAllAsync();
            beforeDispose?.Invoke();
            foreach (var id in _accounts.Keys.ToArray()) await ReleaseAsync(id, true);
        }
        catch { IsStopping = false; throw; }
    }
    public bool HasFolderConflict(IEnumerable<string> folders) => _accounts.Values.SelectMany(runtime => runtime.ActiveFolders).Any(active => folders.Any(folder => Overlap(active, folder)));
    private static bool Overlap(string left, string right) => FolderPaths.Comparer.Equals(left, right) ||
        left.StartsWith(Path.TrimEndingDirectorySeparator(right) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        right.StartsWith(Path.TrimEndingDirectorySeparator(left) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

public sealed record AccountSnapshot(string Usage, string Credits, string Tasks, string Connection, string Tooltip, long? AvailableResetCount = null);
