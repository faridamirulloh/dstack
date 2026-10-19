using System.Windows;
using Microsoft.Web.WebView2.Core;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.Views;

public partial class AccountBrowserWindow : Window
{
    private readonly string _profileName;
    private readonly string _identity;
    private readonly string _userDataFolder;
    private readonly Uri _initialUrl;
    private readonly bool _navigateOnLoad;
    private CoreWebView2Environment? _environment;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;
    private bool _initializing;
    public Task<bool> Ready => _ready.Task;

    public AccountBrowserWindow(string profileName, string identity, Uri url, string userDataFolder)
        : this(profileName, identity, url, userDataFolder, null, true) { }

    private AccountBrowserWindow(string profileName, string identity, Uri url, string userDataFolder, CoreWebView2Environment? environment, bool navigateOnLoad)
    {
        if (!UsageResetPage.CanNavigate(url.AbsoluteUri)) throw new ArgumentException("Only HTTPS pages can be opened.");
        _profileName = profileName; _identity = identity; _initialUrl = url; _userDataFolder = Path.GetFullPath(userDataFolder);
        _environment = environment; _navigateOnLoad = navigateOnLoad;
        InitializeComponent();
        Title = "Usage resets · " + profileName;
        Address.Text = url.AbsoluteUri; Address.ToolTip = url.AbsoluteUri;
        AccountHint.Text = profileName + (identity.Length > 0 ? " · " + identity : "") + ". Website sign-in is separate; use this account on the page.";
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += InitializeBrowser;
        Closed += (_, _) => { _closed = true; _ready.TrySetResult(false); Browser.Dispose(); };
    }

    private async void InitializeBrowser(object sender, RoutedEventArgs e)
    {
        if (_initializing || _closed) return;
        _initializing = true;
        try
        {
            _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            if (_closed) return;
            // WebView2 policy/environment overrides must not merge different accounts' browser sessions.
            if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(_environment.UserDataFolder)).Equals(Path.TrimEndingDirectorySeparator(_userDataFolder), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Windows selected a different browser profile folder. Use Open in browser to continue.");
            await Browser.EnsureCoreWebView2Async(_environment);
            if (_closed) return;
            var core = Browser.CoreWebView2;
            core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.NavigationStarting += (_, args) =>
            {
                if (!UsageResetPage.CanNavigate(args.Uri)) { args.Cancel = true; PageStatus.Text = "This link cannot open here. Use Open in browser for the usage page."; return; }
                PageStatus.Text = "Loading…";
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (_closed) return;
                PageStatus.Text = args.IsSuccess ? "Complete the reset on the website. Close this window to refresh the account's limits." : "Could not load the page (" + args.WebErrorStatus + "). Try Reload or Open in browser.";
            };
            core.SourceChanged += (_, _) => UpdateAddress();
            core.HistoryChanged += (_, _) => { if (!_closed) BackButton.IsEnabled = core.CanGoBack; };
            core.NewWindowRequested += OpenPopup;
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            core.DownloadStarting += (_, args) => { args.Cancel = true; PageStatus.Text = "For downloads, use Open in browser."; };
            _ready.TrySetResult(true);
            if (_navigateOnLoad) core.Navigate(_initialUrl.AbsoluteUri);
            else PageStatus.Text = "Ready";
        }
        catch (Exception error)
        {
            _ready.TrySetResult(false);
            if (!_closed) PageStatus.Text = "The in-app browser is unavailable. Use Open in browser. " + error.Message;
        }
    }

    private async void OpenPopup(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (_closed || !UsageResetPage.CanNavigate(e.Uri)) return;
        using var deferral = e.GetDeferral();
        AccountBrowserWindow? popup = null;
        try
        {
            popup = new AccountBrowserWindow(_profileName, _identity, new Uri(e.Uri), _userDataFolder, _environment, false) { Owner = this };
            popup.Show();
            if (await popup.Ready && !_closed) e.NewWindow = popup.Browser.CoreWebView2;
            else popup.Close();
        }
        catch (Exception error)
        {
            popup?.Close();
            if (!_closed) PageStatus.Text = "Could not open this page here. Use Open in browser. " + error.Message;
        }
    }

    private void UpdateAddress()
    {
        if (_closed) return;
        Address.Text = Browser.CoreWebView2.Source; Address.ToolTip = Address.Text;
    }
    private void GoBack(object sender, RoutedEventArgs e) { if (Browser.CoreWebView2 is { CanGoBack: true } core) core.GoBack(); }
    private void Reload(object sender, RoutedEventArgs e) { if (Browser.CoreWebView2 is { } core) core.Reload(); }
    private void GoToUsage(object sender, RoutedEventArgs e) { if (Browser.CoreWebView2 is { } core) core.Navigate(UsageResetPage.Url.AbsoluteUri); }
    private void OpenExternal(object sender, RoutedEventArgs e)
    {
        try { UsageResetPage.OpenExternally(UsageResetPage.TryWebUrl(Browser.CoreWebView2?.Source, out var current) ? current! : UsageResetPage.Url); }
        catch (Exception error) { AppDialog.Show(this, error.Message, "Could not open browser", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
