using MiniCodex.Presentation;
using System.Windows;
using MiniCodex.Views;
using MiniCodex.Codex;
using MiniCodex.Models;

namespace MiniCodex.Services;

public sealed partial class WindowManager(AppController controller, DraftStore? drafts = null)
{
    private readonly Dictionary<Guid, List<AccountPane>> _accounts = [];
    private readonly List<AccountWindow> _accountWindows = [];
    private AccountWindow? _accountTarget;
    private AccountManagerWindow? _manager;
    public bool IsExiting { get; private set; }
    private bool _exitPending;
    public DraftStore Drafts { get; } = drafts ?? new(controller);
    private RuntimeRegistry? _runtimes;
    public RuntimeRegistry Runtimes => _runtimes ??= new(controller, Drafts);
    private NotificationService? _notifications;
    public NotificationService Notifications => _notifications ??= new(controller, this);
    private SessionCatalog? _sessions;
    public SessionCatalog Sessions => _sessions ??= new SessionCatalog(controller, Runtimes, HasAccountWindow);
    public event EventHandler? Changed;

    public async void OpenAccount(Guid profileId)
    {
        try { await OpenAccountAsync(profileId); }
        catch (Exception error) { AppDialog.Show(error.Message, "Could not open account", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    public async Task FocusAccountAsync(Guid profileId)
    {
        if (!IsExiting && !Runtimes.IsStopping && !Runtimes.IsRemoving(profileId) && controller.State.Profiles.Any(profile => profile.Id == profileId))
            await OpenAccountAsync(profileId, moveExisting: false);
    }
    internal async Task<AccountPane> OpenAccountAsync(Guid profileId, bool newWindow = false, bool moveExisting = true, AccountWindow? targetWindow = null, bool sideBySide = false)
    {
        await Runtimes.WaitForReleaseAsync(profileId);
        if (IsExiting || Runtimes.IsStopping) throw new InvalidOperationException("Mini Codex is exiting.");
        if (Runtimes.IsRemoving(profileId)) throw new InvalidOperationException("This account is being removed.");
        _ = controller.Profile(profileId);
        AccountWindow? target = targetWindow;
        if (target is null && !newWindow && sideBySide)
            target = _accountTarget is { IsClosing: false } current && _accountWindows.Contains(current)
                ? current : _accountWindows.LastOrDefault(candidate => !candidate.IsClosing);
        if (_accounts.TryGetValue(profileId, out var closing) && closing.Count > 0 && closing.All(candidate => candidate.IsClosing))
            throw new InvalidOperationException("This account's windows are closing. Wait for them to finish.");
        if (_accounts.TryGetValue(profileId, out var windows) && !newWindow)
        {
            var existing = windows.LastOrDefault(candidate => !candidate.IsClosing && ReferenceEquals(candidate.HostWindow, target))
                ?? windows.LastOrDefault(candidate => !candidate.IsClosing) ?? throw new InvalidOperationException("This account window is closing. Wait for it to finish.");
            if (moveExisting && target is not null && !ReferenceEquals(existing.HostWindow, target)) MovePane(existing, target);
            existing.HostWindow.FocusPane(existing);
            return existing;
        }
        var pane = new AccountPane(controller, this, profileId);
        if (!_accounts.TryGetValue(profileId, out windows)) _accounts[profileId] = windows = [];
        windows.Add(pane);
        pane.Closed += (_, _) =>
        {
            windows.Remove(pane);
            if (windows.Count == 0) { _accounts.Remove(profileId); CloseUsageResets(profileId); }
            Changed?.Invoke(this, EventArgs.Empty);
            if (!pane.HostWindow.IsClosing && !IsExiting && !_exitPending) RememberAccountLayout();
            if (!IsExiting && _accounts.Count == 0 && _manager is null) Exit();
        };
        var host = target ?? CreateAccountWindow();
        host.AddPane(pane);
        host.FocusPane(pane);
        Changed?.Invoke(this, EventArgs.Empty);
        RememberAccountLayout();
        return pane;
    }

    private AccountWindow CreateAccountWindow()
    {
        var window = new AccountWindow(controller, this);
        _accountWindows.Add(window);
        window.Activated += (_, _) => _accountTarget = window;
        window.Closed += (_, _) =>
        {
            _accountWindows.Remove(window);
            if (ReferenceEquals(_accountTarget, window)) _accountTarget = _accountWindows.LastOrDefault(candidate => !candidate.IsClosing);
        };
        return window;
    }

    private void MovePane(AccountPane pane, AccountWindow target)
    {
        var source = pane.HostWindow;
        if (ReferenceEquals(source, target)) return;
        if (pane.IsClosing || source.IsClosing || target.IsClosing) throw new InvalidOperationException("Wait for this account window to finish closing.");
        pane.AttachTo(target);
        if (_usageBrowsers.TryGetValue(pane.ProfileId, out var browser) && ReferenceEquals(browser.Owner, source)) browser.Owner = target;
        source.RemovePane(pane);
        target.AddPane(pane);
        Changed?.Invoke(this, EventArgs.Empty);
        RememberAccountLayout();
    }

    public void DetachPane(AccountPane pane)
    {
        if (IsExiting || Runtimes.IsStopping || pane.IsClosing || pane.HostWindow.IsClosing) return;
        if (pane.HostWindow.Panes.Count < 2) return;
        var target = CreateAccountWindow();
        target.Show();
        MovePane(pane, target);
        target.FocusPane(pane);
    }

    public async Task OpenSessionAsync(SessionListItem session, bool newWindow = false)
    {
        if (IsExiting || Runtimes.IsStopping) throw new InvalidOperationException("Mini Codex is exiting.");
        if (Runtimes.IsRemoving(session.ProfileId)) throw new InvalidOperationException("This account is being removed.");
        var target = controller.RegisterSession(session);
        var window = await OpenAccountAsync(session.ProfileId, newWindow);
        window.SelectChat(target.ProjectId, target.ChatId);
    }
    public async Task OpenChatAsync(Guid profileId, Guid chatId, string? requestKey = null)
    {
        var profile = controller.State.Profiles.FirstOrDefault(profile => profile.Id == profileId);
        var project = profile?.Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == chatId));
        if (project is null) return;
        var window = await OpenAccountAsync(profileId, moveExisting: false); window.SelectChat(project.Id, chatId);
        if (requestKey is not null) await window.ReviewRequestAsync(requestKey);
    }

    public async Task SignInAccountAsync(Guid profileId)
    {
        var window = await OpenAccountAsync(profileId);
        await window.SignInAsync();
    }

    public async Task<bool> RemoveAccountAsync(Guid profileId)
    {
        if (IsExiting || Runtimes.IsStopping) throw new InvalidOperationException("Mini Codex is exiting.");
        Runtimes.BeginRemoval(profileId);
        var removed = false;
        try
        {
            if (_accounts.TryGetValue(profileId, out var windows))
                foreach (var window in windows.ToArray()) if (!await window.CloseAccountAsync()) return false;
            await Runtimes.ReleaseAsync(profileId, true);
            CloseUsageResets(profileId);
            controller.RemoveProfile(profileId);
            removed = true;
            _sessions?.ForgetProfile(profileId);
            return true;
        }
        finally { Runtimes.EndRemoval(profileId, removed); }
    }

    public void ShowAccounts(AccountWindow? source = null, FrameworkElement? placementTarget = null)
    {
        if (source is { IsClosing: false }) _accountTarget = source;
        if (_manager is not null)
        {
            if (_manager.WindowState == WindowState.Minimized) _manager.WindowState = WindowState.Normal;
            _manager.Show(); DialogPlacement.CenterOn(_manager, placementTarget); _manager.Activate(); return;
        }
        _manager = new AccountManagerWindow(controller, this);
        DialogPlacement.CenterOn(_manager, placementTarget);
        _manager.Closed += (_, _) => { _manager = null; if (_accounts.Count == 0 && !IsExiting) Exit(); };
        _manager.Show();
    }

    public bool HasAccountWindow(Guid id) => _accounts.ContainsKey(id);
    public bool HasOtherAccountWindow(Guid id, AccountPane current) => _accounts.TryGetValue(id, out var windows) && windows.Any(window => !ReferenceEquals(window, current) && !window.IsClosing);

    public async Task<bool> EditAccountAsync(Guid id, string name, string home)
    {
        var current = CodexInstallation.Home(controller.Profile(id));
        var target = Directory.Exists(home) ? FolderPaths.Canonicalize(home) : Path.GetFullPath(home);
        if (FolderPaths.Comparer.Equals(current, target)) { controller.EditProfile(id, name, target); return true; }
        if (!Directory.Exists(target)) throw new IOException("Choose an existing Codex home.");
        foreach (var profile in controller.State.Profiles.Where(profile => profile.Id != id))
        {
            var other = CodexInstallation.Home(profile);
            if (Directory.Exists(other)) other = FolderPaths.Canonicalize(other);
            if (FolderPaths.Comparer.Equals(other, target)) throw new InvalidOperationException("That home is already connected to another account.");
        }
        Runtimes.BeginRemoval(id);
        try
        {
            if (_accounts.TryGetValue(id, out var windows))
                foreach (var window in windows.ToArray()) if (!await window.CloseAccountAsync()) return false;
            await Runtimes.ReleaseAsync(id, true);
            CloseUsageResets(id);
            controller.EditProfile(id, name, target);
            Runtimes.ForgetAuthentication(id); _sessions?.ForgetProfile(id);
            return true;
        }
        finally { Runtimes.EndRemoval(id, false); }
    }
    public async void Exit(FrameworkElement? placementTarget = null)
    {
        if (_exitPending || IsExiting) return;
        _exitPending = true;
        try
        {
            if (Runtimes.All.Any(runtime => runtime.InstallingExtensions)) throw new InvalidOperationException("Wait for extension installation to finish before exiting Mini Codex.");
            if (Runtimes.All.Any(runtime => runtime.HasWork) && AppDialog.Show("Stop all active tasks and exit Mini Codex? Cancel keeps the app and tasks running.", "Exit Mini Codex", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel, placementTarget) != MessageBoxResult.OK) return;
            if (!RememberAccountLayout()) return;
            await Runtimes.StopAndDisposeAsync(Drafts.Flush);
            _notifications?.Dispose();
            IsExiting = true; Application.Current.Shutdown();
        }
        catch (Exception error) { AppDialog.Show(error.Message + "\nThe application remains open.", "Could not stop Codex", MessageBoxButton.OK, MessageBoxImage.Warning, placementTarget: placementTarget); }
        finally { _exitPending = false; }
    }
}
