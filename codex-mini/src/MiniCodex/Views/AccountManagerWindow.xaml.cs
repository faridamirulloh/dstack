using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.Codex;
using Microsoft.Win32;
using System.Windows.Threading;

namespace MiniCodex.Views;

public partial class AccountManagerWindow : Window
{
    private readonly AppController _controller;
    private readonly WindowManager _windows;
    private readonly Dictionary<Guid, string> _busy = [];
    private readonly HashSet<Guid> _checking = [];
    private readonly Dictionary<Guid, (DateTimeOffset At, string Message)> _checkErrors = [];
    private bool _checkingAll;
    private bool _closed;
    private readonly DispatcherTimer _overviewRefresh = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public AccountManagerWindow(AppController controller, WindowManager windows)
    {
        _controller = controller; _windows = windows;
        InitializeComponent();
        _overviewRefresh.Tick += (_, _) => { _overviewRefresh.Stop(); RefreshRows(); };
        _controller.Changed += Refresh;
        _windows.Changed += Refresh;
        _windows.Runtimes.Updated += Refresh;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += async (_, _) => await CheckAccountsAsync();
        Closed += (_, _) => { _closed = true; _overviewRefresh.Stop(); _controller.Changed -= Refresh; _windows.Changed -= Refresh; _windows.Runtimes.Updated -= Refresh; };
        Refresh(this, EventArgs.Empty);
    }

    private void Refresh(object? sender, EventArgs e) { if (!_closed && !_overviewRefresh.IsEnabled) _overviewRefresh.Start(); }
    private void RefreshRows() => DataContext = new
    {
        Profiles = _controller.State.Profiles.Select(profile => CreateRow(profile.Id)).ToArray(),
        CanRefreshAccounts = !_checkingAll && !_windows.Runtimes.IsStopping
    };

    private AccountRow CreateRow(Guid id)
    {
        var profile = _controller.Profile(id);
        var runtime = _windows.Runtimes.Find(id);
        var snapshot = _windows.Runtimes.Snapshot(id);
        var queued = _windows.Drafts.For(id).Queues.Values.Sum(queue => queue.Count);
        var authentication = _windows.Runtimes.Authentication(id);
        var checking = _checking.Contains(id);
        var busy = _busy.ContainsKey(id) || _windows.Runtimes.IsRemoving(id);
        var error = CheckError(id, authentication);
        var needsCheck = authentication is null;
        var status = authentication is null ? "Sign-in not checked" : authentication.SignedIn ? "Signed in · " + authentication.Identity : "Signed out";
        if (runtime?.LoginPending == true) status = "Waiting for browser sign-in";
        else if (checking) status += " · Checking…";
        else if (error is not null) status += " · Check failed";
        else if (runtime?.Connected != true && authentication is not null) status += " · Last checked";
        if (runtime?.HasWork == true) status += " · " + runtime.TaskSummary;
        var authLabel = _busy.GetValueOrDefault(id) ?? (checking ? "Checking…" : runtime?.LoginPending == true ? "Continue sign-in" : needsCheck ? "Check sign-in" : authentication!.SignedIn ? "Sign out" : "Sign in");
        var details = CodexInstallation.Home(profile) + "\n" + (authentication is null ? "Sign-in has not been confirmed." : "Last checked " + authentication.CheckedAt.ToLocalTime().ToString("g"));
        if (error is not null) details += "\n" + error;
        if (runtime?.HasWork == true) details += "\nStop this account's tasks before changing sign-in.";
        return new AccountRow(id, profile.Name, profile.Initials, _windows.HasAccountWindow(id) ? "Focus account" : "Open account", status, details, authLabel, needsCheck,
            !busy && !checking && runtime?.HasWork != true, !busy && !checking, !busy && !checking,
            runtime?.Usage ?? snapshot?.Usage ?? "Usage not loaded", runtime?.Credits ?? snapshot?.Credits ?? "Credits not loaded",
            runtime?.TaskSummary ?? ("No active tasks" + (queued > 0 ? $" · {queued} queued" : "")), runtime?.Status ?? "Not connected", runtime?.UsageTooltip ?? snapshot?.Tooltip ?? "Refresh accounts to load usage.",
            runtime is not null ? runtime.AvailableResetCount : snapshot?.AvailableResetCount);
    }

    private string? CheckError(Guid id, AccountAuthentication? authentication) =>
        _checkErrors.TryGetValue(id, out var error) && (authentication is null || error.At >= authentication.CheckedAt) ? error.Message : null;

    private async void RefreshAccounts(object sender, RoutedEventArgs e) => await CheckAccountsAsync();

    private async Task CheckAccountsAsync()
    {
        if (_checkingAll || _closed) return;
        _checkingAll = true; Refresh(this, EventArgs.Empty);
        try
        {
            foreach (var id in _controller.State.Profiles.Select(profile => profile.Id).ToArray())
            {
                if (_closed || _windows.Runtimes.IsStopping) break;
                await CheckAccountAsync(id);
            }
        }
        finally { _checkingAll = false; if (!_closed) Refresh(this, EventArgs.Empty); }
    }

    private async Task CheckAccountAsync(Guid id)
    {
        if (_busy.ContainsKey(id) || _windows.Runtimes.IsRemoving(id) || !_controller.State.Profiles.Any(profile => profile.Id == id) || !_checking.Add(id)) return;
        _checkErrors.Remove(id); Refresh(this, EventArgs.Empty);
        RuntimeRegistry.RuntimeLease? lease = null;
        try
        {
            lease = await _windows.Runtimes.BorrowAsync(id);
            if (lease.Runtime.Connected) await lease.Runtime.RefreshAuthenticationAsync();
            else await lease.Runtime.ConnectAsync(false);
            if (lease.Runtime.SignedIn) await lease.Runtime.RefreshUsageAsync();
        }
        catch (Exception error) { _checkErrors[id] = (DateTimeOffset.Now, error.Message); }
        finally
        {
            if (lease is not null)
            {
                try { await ReturnLeaseAsync(id, lease); }
                catch (Exception error) { _checkErrors[id] = (DateTimeOffset.Now, error.Message); }
            }
            _checking.Remove(id);
            if (!_closed) Refresh(this, EventArgs.Empty);
        }
    }

    private async Task ReturnLeaseAsync(Guid id, RuntimeRegistry.RuntimeLease lease)
    {
        try
        {
            if (!_windows.HasAccountWindow(id) && !lease.Runtime.HasWork) await _windows.Runtimes.ReleaseAsync(id);
        }
        finally { await lease.DisposeAsync(); }
    }

    private void OpenAccountOptions(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
    }

    private async void OpenAccount(object sender, RoutedEventArgs e) => await OpenAccountCoreAsync(sender);
    private async void OpenAccountSideBySide(object sender, RoutedEventArgs e) => await OpenAccountCoreAsync(sender, sideBySide: true);
    private async void OpenAccountWindow(object sender, RoutedEventArgs e) => await OpenAccountCoreAsync(sender, newWindow: true);

    private async Task OpenAccountCoreAsync(object sender, bool newWindow = false, bool sideBySide = false)
    {
        if ((sender as FrameworkElement)?.DataContext is not AccountRow { CanOpen: true } profile) return;
        try
        {
            await _windows.OpenAccountAsync(profile.Id, newWindow: newWindow, sideBySide: sideBySide);
            if (!_closed) Close();
        }
        catch (Exception error)
        {
            if (!_closed) AppDialog.Show(this, error.Message, "Could not open account", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void OpenUsageResets(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AccountRow row) return;
        try { _windows.OpenUsageResets(row.Id, this); }
        catch (Exception error) { AppDialog.Show(this, error.Message, "Usage resets", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void AddAccount(object sender, RoutedEventArgs e)
    {
        var dialog = new TextPromptWindow("Add account profile", "Display name", $"Account {_controller.State.Profiles.Count + 1}") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { _windows.OpenAccount(_controller.AddProfile(dialog.Value)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { AppDialog.Show(this, error.Message, "Account could not be added", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ConnectCodexHome(object sender, RoutedEventArgs e)
    {
        var initial = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var picker = new OpenFolderDialog { Title = "Choose the Codex home used by Desktop or CLI", Multiselect = false };
        if (Directory.Exists(initial)) picker.InitialDirectory = initial;
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var home = FolderPaths.Canonicalize(picker.FolderName);
            foreach (var profile in _controller.State.Profiles)
                if (Directory.Exists(CodexInstallation.Home(profile)) && FolderPaths.Comparer.Equals(home, FolderPaths.Canonicalize(CodexInstallation.Home(profile))))
                { _windows.OpenAccount(profile.Id); return; }
            var name = new TextPromptWindow("Connect existing Codex home", "Account display name", "Existing Codex") { Owner = this };
            if (name.ShowDialog() != true) return;
            if (AppDialog.Show(this, "Connect this home?\n" + home + "\n\nThis uses its existing login, credentials policy, and conversation history. Codex can make its normal writes there. Sign out here also signs this home out in other clients. Use each conversation in one client at a time.\n\nA new Mini Codex profile will keep your current profiles intact.", "Connect Codex home", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            _windows.OpenAccount(_controller.AddProfile(name.Value, home));
        }
        catch (Exception error)
        { AppDialog.Show(this, error.Message, "Could not connect Codex home", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void Authenticate(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AccountRow row || !row.CanAuthenticate || _busy.ContainsKey(row.Id)) return;
        if (row.NeedsCheck) { await CheckAccountAsync(row.Id); return; }
        var signingIn = _windows.Runtimes.Find(row.Id)?.LoginPending == true || _windows.Runtimes.Authentication(row.Id)?.SignedIn == false;
        _busy[row.Id] = signingIn ? "Signing in…" : "Signing out…";
        Refresh(this, EventArgs.Empty);
        RuntimeRegistry.RuntimeLease? lease = null;
        try
        {
            if (signingIn) { await _windows.SignInAccountAsync(row.Id); return; }
            lease = await _windows.Runtimes.BorrowAsync(row.Id);
            var runtime = lease.Runtime;
            if (runtime.HasWork) throw new InvalidOperationException("Stop this account's tasks before signing out.");
            if (runtime.Connected) await runtime.RefreshAuthenticationAsync();
            else await runtime.ConnectAsync(false);
            if (!runtime.SignedIn) return;
            if (_controller.Profile(row.Id).ExistingCodexHome is not null && AppDialog.Show(this, "Sign out this shared Codex home? Desktop and CLI clients using it will also lose this home's saved sign-in.", "Sign out", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            await runtime.LogoutAsync();
        }
        catch (Exception error)
        {
            lease?.Runtime.Report(error);
            if (!_closed) AppDialog.Show(this, error.Message, "Could not change sign-in", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (lease is not null)
            {
                try
                {
                    await ReturnLeaseAsync(row.Id, lease);
                }
                catch (Exception error) { lease.Runtime.Report(error); }
            }
            _busy.Remove(row.Id);
            if (!_closed) Refresh(this, EventArgs.Empty);
        }
    }

    private async void EditAccount(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AccountRow row || !row.CanOpen || _busy.ContainsKey(row.Id)) return;
        var current = _controller.Profile(row.Id);
        var dialog = new EditAccountWindow(current.Name, CodexInstallation.Home(current)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _busy[row.Id] = "Saving…"; Refresh(this, EventArgs.Empty);
        var saved = false;
        try { saved = await _windows.EditAccountAsync(row.Id, dialog.DisplayName, dialog.CodexHome); }
        catch (Exception error) { if (!_closed) AppDialog.Show(this, error.Message, "Could not edit account", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _busy.Remove(row.Id); if (!_closed) Refresh(this, EventArgs.Empty); }
        if (saved && !_closed) await CheckAccountAsync(row.Id);
    }

    private async void RemoveAccount(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AccountRow row || !row.CanRemove || _busy.ContainsKey(row.Id)) return;
        var home = CodexInstallation.Home(_controller.Profile(row.Id));
        if (AppDialog.Show(this, $"Remove '{row.Name}' from Mini Codex?\n\nIts windows will close. Local project settings, chat references, saved drafts, and queued messages will be removed. Its Codex home, saved login, and session history are kept:\n{home}", "Remove account", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        _busy[row.Id] = "Removing…"; Refresh(this, EventArgs.Empty);
        try
        {
            if (await _windows.RemoveAccountAsync(row.Id)) _checkErrors.Remove(row.Id);
        }
        catch (Exception error)
        {
            if (!_closed) AppDialog.Show(this, error.Message, "Could not remove account", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _busy.Remove(row.Id); if (!_closed) Refresh(this, EventArgs.Empty); }
    }

    public sealed record AccountRow(Guid Id, string Name, string Initials, string ActionLabel, string Status, string Details, string AuthLabel, bool NeedsCheck, bool CanAuthenticate, bool CanOpen, bool CanRemove,
        string Usage, string Credits, string Tasks, string Connection, string UsageDetails, long? AvailableResetCount = null)
    {
        public bool HasResetCount => AvailableResetCount is not null;
        public string ResetLabel => AvailableResetCount is { } count ? $"{count} rst" : "";
    }
}
