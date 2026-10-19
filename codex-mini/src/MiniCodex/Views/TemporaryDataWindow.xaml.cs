using System.Windows;
using Microsoft.Web.WebView2.Core;
using MiniCodex.Codex;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.Views;

public partial class TemporaryDataWindow : Window
{
    private readonly AppController _controller;
    private readonly AccountRuntime _runtime;
    private readonly string _home;
    private readonly string _browserFolder;
    private readonly CancellationTokenSource _lifetime = new();
    private TemporaryDataSnapshot? _snapshot;
    private bool _busy;
    private bool _browserReady;
    private bool _closed;

    public TemporaryDataWindow(AppController controller, AccountRuntime runtime)
    {
        _controller = controller; _runtime = runtime;
        var profile = controller.Profile(runtime.ProfileId);
        _home = Path.GetFullPath(CodexInstallation.Home(profile));
        _browserFolder = UsageResetPage.BrowserFolder(profile.Id, _home);
        InitializeComponent(); Scope.Text = profile.Name + " · This account only";
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += Load;
        _runtime.Updated += RuntimeUpdated;
        _controller.Changed += AppStateChanged;
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        Closed += (_, _) =>
        {
            _closed = true; _lifetime.Cancel(); CacheBrowser.Dispose();
            _runtime.Updated -= RuntimeUpdated; _controller.Changed -= AppStateChanged;
        };
    }

    private bool SameHome => _controller.State.Profiles.FirstOrDefault(profile => profile.Id == _runtime.ProfileId) is { } profile
        && FolderPaths.Comparer.Equals(Path.GetFullPath(CodexInstallation.Home(profile)), _home);
    private bool Idle => !_runtime.HasWork && !_runtime.HasQueuedMessages && !_runtime.LoginPending && _runtime.Approvals.Count == 0;
    private void RuntimeUpdated(object? sender, EventArgs e) => UpdateAvailability();
    private void AppStateChanged(object? sender, EventArgs e)
    {
        if (!SameHome) { _lifetime.Cancel(); Status.Text = "This account changed or was removed. Close this dialog and reopen it from the account's gear menu."; }
        UpdateAvailability();
    }
    private void SelectionChanged(object sender, RoutedEventArgs e) => UpdateAvailability();
    private void UpdateAvailability()
    {
        if (_closed || ClearButton is null) return;
        ClearButton.IsEnabled = !_busy && !_lifetime.IsCancellationRequested && SameHome && Idle && _snapshot is not null
            && (_snapshot.FileCount > 0 || BrowserCache.IsEnabled && BrowserCache.IsChecked == true);
        ActivityHint.Text = !_busy && SameHome && !Idle
            ? "Finish this account's tasks, queued messages, sign-in, and reviews before clearing temporary data." : "";
    }

    private async void Load(object sender, RoutedEventArgs e)
    {
        _busy = true; CloseButton.IsEnabled = false;
        try
        {
            _snapshot = await Task.Run(() => TemporaryDataCleanup.Scan(_home, _lifetime.Token));
            _lifetime.Token.ThrowIfCancellationRequested();
            ShowSummary();
            if (Directory.Exists(_browserFolder))
            {
                try
                {
                    TemporaryDataCleanup.EnsurePlainPath(_browserFolder, _browserFolder);
                    var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _browserFolder);
                    _lifetime.Token.ThrowIfCancellationRequested();
                    if (!FolderPaths.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.UserDataFolder)), Path.TrimEndingDirectorySeparator(_browserFolder)))
                        throw new IOException("Windows selected a different browser profile. Its cache has been kept.");
                    await CacheBrowser.EnsureCoreWebView2Async(environment);
                    _lifetime.Token.ThrowIfCancellationRequested();
                    CacheBrowser.CoreWebView2.Settings.IsWebMessageEnabled = false;
                    CacheBrowser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                    CacheBrowser.CoreWebView2.NavigationStarting += (_, args) => args.Cancel = true;
                    _browserReady = true; BrowserCache.IsEnabled = true; BrowserCache.IsChecked = true;
                    BrowserHint.Text = "Clears cached website files. Website sign-in is kept. Cache size is unavailable and excluded from the file total.";
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { BrowserHint.Text = "Browser cache is unavailable and will be kept. " + error.Message; }
            }
            else BrowserHint.Text = "No browser cache exists for this account.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Status.Text = error.Message; }
        finally { _busy = false; CloseButton.IsEnabled = true; UpdateAvailability(); }
    }

    private void ShowSummary()
    {
        if (_snapshot is null) return;
        var categories = _snapshot.Batches.GroupBy(batch => batch.Category)
            .Select(group => group.Key + " · " + TemporaryDataCleanup.Size(group.Sum(batch => batch.Files.Sum(file => file.Bytes))));
        FileSummary.Text = _snapshot.FileCount == 0 ? "No removable temporary files found."
            : string.Join("\n", categories) + $"\n\nEstimated file total · {TemporaryDataCleanup.Size(_snapshot.Bytes)} · {_snapshot.FileCount} files";
        Status.Text = string.Join("\n", _snapshot.Notes);
    }

    private async void Clear(object sender, RoutedEventArgs e)
    {
        var snapshot = _snapshot;
        if (_busy || _lifetime.IsCancellationRequested || !SameHome || !Idle || snapshot is null) return;
        _busy = true; ClearButton.IsEnabled = false; CloseButton.IsEnabled = false;
        var clearBrowser = BrowserCache.IsEnabled && BrowserCache.IsChecked == true;
        BrowserCache.IsEnabled = false; Status.Text = "Clearing temporary data…";
        try
        {
            var result = await Task.Run(() => TemporaryDataCleanup.Clear(snapshot, _lifetime.Token));
            _lifetime.Token.ThrowIfCancellationRequested();
            var browserResult = "";
            if (clearBrowser && SameHome)
            {
                try
                {
                    await CacheBrowser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
                    browserResult = "\nBrowser disk cache cleared.";
                }
                catch (Exception error) { browserResult = "\nBrowser cache could not be cleared: " + error.Message; }
            }
            _snapshot = await Task.Run(() => TemporaryDataCleanup.Scan(_home, _lifetime.Token));
            ShowSummary();
            var notes = result.Notes.Concat(_snapshot.Notes).Distinct().ToArray();
            Status.Text = $"Removed {result.Deleted} files ({TemporaryDataCleanup.Size(result.Bytes)})."
                + (result.Skipped > 0 ? $" Kept {result.Skipped} files that changed or are in use." : "")
                + browserResult + (notes.Length > 0 ? "\n" + string.Join("\n", notes) : "");
            CloseButton.Content = "Close";
        }
        catch (OperationCanceledException) { Status.Text = "Cleanup stopped because this account changed. Some temporary files may already have been removed."; }
        catch (Exception error) { Status.Text = error.Message; }
        finally
        {
            _busy = false; CloseButton.IsEnabled = true;
            BrowserCache.IsEnabled = _browserReady && SameHome && !_lifetime.IsCancellationRequested;
            UpdateAvailability();
        }
    }
}
