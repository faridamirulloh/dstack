using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MiniCodex.Presentation;

internal sealed class VisualizationPreview : Grid
{
    private static readonly string BrowserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCodex", "visualizations", "browser");
    private static readonly Dictionary<string, string> States = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ResourceHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdnjs.cloudflare.com", "cdn.jsdelivr.net", "esm.sh", "fonts.bunny.net", "fonts.googleapis.com", "fonts.gstatic.com", "unpkg.com"
    };
    private const string ResourceSources = "blob: data: https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://esm.sh https://fonts.bunny.net https://fonts.googleapis.com https://fonts.gstatic.com https://unpkg.com";
    private const string FramePolicy = "default-src 'none'; script-src 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' " + ResourceSources
        + "; style-src 'unsafe-inline' " + ResourceSources + "; img-src " + ResourceSources + "; font-src " + ResourceSources
        + "; media-src " + ResourceSources + "; worker-src blob:; connect-src blob: data:; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
    private static Task<CoreWebView2Environment>? _environment;
    private readonly string? _path;
    private readonly string _title;
    private readonly Grid _body = new() { MinHeight = 100 };
    private readonly TextBlock _status = new() { Text = "Loading preview…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Center };
    private WebView2? _browser;
    private (IntPtr Window, Int32Rect Bounds)? _browserClip;
    private bool _releaseQueued;
    private string? _stateKey;

    public VisualizationPreview(string target, string title, string workingDirectory)
    {
        _title = title;
        if (FileLinks.TryResolve(target, workingDirectory, out var path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
            && Path.GetExtension(path).ToLowerInvariant() is ".html" or ".htm") _path = path;
        Margin = new Thickness(0, 7, 0, 10);
        var panel = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(10, 5, 6, 5) };
        var reload = new Button { Content = "Reload", Style = (Style)Application.Current.FindResource("QuietButton"), FontSize = 11, Padding = new Thickness(7, 3, 7, 3), ToolTip = "Reload the visualization file" };
        DockPanel.SetDock(reload, Dock.Right); header.Children.Add(reload);
        reload.Click += (_, e) => { e.Handled = true; ReleaseBrowser(); LoadPreview(); };
        var caption = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        if (_path is not null) caption.Inlines.Add(FileLinks.Link(_path, label: title));
        else caption.Text = title;
        header.Children.Add(caption);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _body.Children.Add(_status); panel.Children.Add(_body);
        var border = new Border { Child = panel, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        border.SetResourceReference(Border.BackgroundProperty, "CodeSurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        Children.Add(border);
        AutomationProperties.SetName(this, "Visualization: " + title);
        Loaded += (_, _) => LoadPreview();
        Unloaded += (_, _) => QueueBrowserRelease();
        IsVisibleChanged += (_, _) => { if (IsVisible) LoadPreview(); else QueueBrowserRelease(); };
    }

    private async void LoadPreview()
    {
        if (!IsLoaded || !IsVisible || _browser is not null) return;
        _status.Visibility = Visibility.Visible;
        if (_path is null) { _status.Text = "Preview requires a local HTML file."; return; }
        _status.Text = "Loading preview…";
        WebView2? browser = null;
        try
        {
            // Use windowed hosting to avoid the composition control's failing Direct3D capture path.
            browser = new WebView2 { Height = 320, AllowExternalDrop = false, DefaultBackgroundColor = System.Drawing.Color.FromArgb(24, 24, 24) };
            _browser = browser;
            browser.LayoutUpdated += UpdateBrowserClip;
            _body.Children.Insert(0, browser);
            var snapshot = await Task.Run(() => ReadFragment(_path));
            if (!ReferenceEquals(_browser, browser)) return;
            _stateKey = snapshot.Key;
            States.TryGetValue(snapshot.Key, out var state);
            var html = BuildDocument(snapshot.Html, _title, state);
            _environment ??= CoreWebView2Environment.CreateAsync(userDataFolder: BrowserFolder);
            CoreWebView2Environment environment;
            try { environment = await _environment; }
            catch { _environment = null; throw; }
            if (!ReferenceEquals(_browser, browser)) return;
            if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.UserDataFolder)).Equals(Path.TrimEndingDirectorySeparator(BrowserFolder), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Windows selected a different browser profile folder.");
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options);
            if (!ReferenceEquals(_browser, browser)) return;
            var core = browser.CoreWebView2;
            core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            var documentUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
            var initialNavigation = true;
            core.NavigationStarting += (_, args) =>
            {
                // Some runtimes expose NavigateToString's generated data URL in this event.
                if (initialNavigation && (args.Uri == "about:blank" || args.Uri == documentUri)) initialNavigation = false;
                else args.Cancel = true;
            };
            core.FrameNavigationStarting += (_, args) => args.Cancel = args.Uri is not ("about:srcdoc" or "about:blank");
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri)
                    || !(uri.Scheme is "about" or "data" or "blob" || uri.Scheme == "https" && ResourceHosts.Contains(uri.IdnHost)))
                    args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            core.WebMessageReceived += (_, args) =>
            {
                if (ReferenceEquals(_browser, browser) && args.Source == "about:blank") ReceiveMessage(core, args.WebMessageAsJson);
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (!ReferenceEquals(_browser, browser)) return;
                _status.Text = args.IsSuccess ? "" : "Could not load preview: " + args.WebErrorStatus;
                _status.Visibility = args.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
            };
            core.NavigateToString(html);
        }
        catch (Exception error)
        {
            if (!ReferenceEquals(_browser, browser)) return;
            ReleaseBrowser();
            _status.Text = "Preview unavailable. " + error.Message;
            _status.Visibility = Visibility.Visible;
        }
    }

    private void ReceiveMessage(CoreWebView2 core, string message)
    {
        if (message.Length > 100_000) return;
        try
        {
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
            switch (type.GetString())
            {
                case "height" when root.TryGetProperty("height", out var height) && height.TryGetDouble(out var pixels) && double.IsFinite(pixels):
                    if (_browser is not null) _browser.Height = Math.Clamp(pixels, 100, 640);
                    break;
                case "widget-state-write" when root.TryGetProperty("id", out var id) && id.TryGetInt64(out var request):
                    var saved = false;
                    if (_stateKey is not null && root.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
                        && state.GetString() is { } serialized && Encoding.UTF8.GetByteCount(serialized) <= 16_384)
                    {
                        using var parsed = JsonDocument.Parse(serialized);
                        if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            if (States.Count >= 128 && !States.ContainsKey(_stateKey)) States.Remove(States.Keys.First());
                            States[_stateKey] = serialized; saved = true;
                        }
                    }
                    core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "widget-state-result", id = request, ok = saved }));
                    break;
                case "follow-up" when root.TryGetProperty("prompt", out var prompt) && prompt.ValueKind == JsonValueKind.String:
                    if (prompt.GetString() is { Length: > 0 and <= 16_000 } text && !string.IsNullOrWhiteSpace(text))
                        RaiseEvent(new VisualizationFollowUpEventArgs(MarkdownMessageView.VisualizationFollowUpRequestedEvent, text));
                    break;
                case "open-external" when root.TryGetProperty("href", out var href) && href.ValueKind == JsonValueKind.String:
                    if (Uri.TryCreate(href.GetString(), UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0)
                        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
                    break;
                case "wheel" when root.TryGetProperty("delta", out var delta) && delta.TryGetDouble(out var scroll) && double.IsFinite(scroll) && scroll != 0:
                    RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, scroll > 0 ? -120 : 120)
                    { RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = this });
                    break;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { _status.Text = "Could not complete preview action. " + error.Message; _status.Visibility = Visibility.Visible; }
    }

    private void QueueBrowserRelease()
    {
        if (_releaseQueued) return;
        _releaseQueued = true;
        // FlowDocument briefly hides embedded controls while reformatting a message.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _releaseQueued = false;
            if (!IsLoaded || !IsVisible) ReleaseBrowser();
        }));
    }

    private void ReleaseBrowser()
    {
        var browser = _browser; _browser = null; _browserClip = null;
        if (browser is null) return;
        browser.LayoutUpdated -= UpdateBrowserClip;
        _body.Children.Remove(browser);
        browser.Dispose();
    }

    private void UpdateBrowserClip(object? sender, EventArgs e)
    {
        if (_browser is not { IsLoaded: true, IsVisible: true } browser || browser.Handle == IntPtr.Zero
            || browser.ActualWidth <= 0 || browser.ActualHeight <= 0) return;
        try
        {
            // A native child window does not inherit WPF's transcript viewport clipping.
            var visible = new Rect(browser.RenderSize);
            for (var parent = VisualTreeHelper.GetParent(browser); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not UIElement element) continue;
                var transform = element.TransformToDescendant(browser);
                if (element.ClipToBounds) visible.Intersect(transform.TransformBounds(new Rect(element.RenderSize)));
                if (VisualTreeHelper.GetClip(element) is { } clip) visible.Intersect(transform.TransformBounds(clip.Bounds));
                if (visible.IsEmpty) break;
            }
            var window = browser.Handle;
            if (!GetClientRect(window, out var client)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            // The actual HWND size includes both monitor DPI and the app's layout zoom.
            var scaleX = client.Right / browser.ActualWidth;
            var scaleY = client.Bottom / browser.ActualHeight;
            var left = visible.IsEmpty ? 0 : Math.Clamp((int)Math.Ceiling(visible.Left * scaleX), 0, client.Right);
            var top = visible.IsEmpty ? 0 : Math.Clamp((int)Math.Ceiling(visible.Top * scaleY), 0, client.Bottom);
            var right = visible.IsEmpty ? 0 : Math.Clamp((int)Math.Floor(visible.Right * scaleX), left, client.Right);
            var bottom = visible.IsEmpty ? 0 : Math.Clamp((int)Math.Floor(visible.Bottom * scaleY), top, client.Bottom);
            var bounds = new Int32Rect(left, top, right - left, bottom - top);
            if (_browserClip is { } previous && previous.Window == window && previous.Bounds == bounds) return;
            var region = CreateRectRgn(left, top, right, bottom);
            if (region == IntPtr.Zero) throw new InvalidOperationException("Could not create the preview's clipping region.");
            if (SetWindowRgn(window, region, true) == 0)
            {
                DeleteObject(region);
                throw new InvalidOperationException("Could not clip the preview to the chat viewport.");
            }
            // Windows owns the region after SetWindowRgn succeeds.
            _browserClip = (window, bounds);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            ReleaseBrowser();
            _status.Text = "Preview unavailable. " + error.Message;
            _status.Visibility = Visibility.Visible;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    protected override Size MeasureOverride(Size constraint)
    {
        var desired = base.MeasureOverride(constraint);
        return new Size(double.IsFinite(constraint.Width) ? constraint.Width : desired.Width, desired.Height);
    }

    private sealed record Snapshot(string Html, string Key);
    private static Snapshot ReadFragment(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 1_000_000) throw new IOException("Visualization files must be under 1 MB.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return new(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'), path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + bytes.Length);
    }

    private static string Asset(string name)
    {
        using var stream = typeof(VisualizationPreview).Assembly.GetManifestResourceStream("MiniCodex.Assets.Visualizations." + name)
            ?? throw new IOException("Missing visualization resource: " + name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string BuildDocument(string fragment, string title, string? state)
    {
        var widgetState = state is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(state);
        var initial = JsonSerializer.Serialize(new { widgetState });
        var content = Asset("visualize.html").Replace("<!--__INLINE_VISUALIZATION_FRAGMENT__-->",
            (fragment.Contains("viz-calendar", StringComparison.OrdinalIgnoreCase) ? "<script>" + Asset("calendar.js").Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase) + "</script>" : "") + fragment, StringComparison.Ordinal);
        var frame = $$"""
            <!doctype html><html lang="en" data-theme="dark"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1"><meta name="referrer" content="no-referrer">
            <meta http-equiv="Content-Security-Policy" content="{{FramePolicy}}"><title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>{{Asset("visualize.css")}}
            :root{--font-size-base:13.5px}html>body{padding:14px}</style></head><body>
            <script type="application/json" id="mini-visualization-state">{{initial}}</script>
            <script>{{Asset("bridge.js")}}</script>{{content}}</body></html>
            """;
        var document = $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="referrer" content="no-referrer">
            <meta http-equiv="Content-Security-Policy" content="{{FramePolicy.Replace("frame-src 'none'", "frame-src 'self' about:", StringComparison.Ordinal)}}">
            <style>html,body{margin:0;background:#181818}iframe{display:block;width:100%;height:100vh;border:0}</style></head><body>
            <iframe id="mini-visualization" sandbox="allow-scripts" referrerpolicy="no-referrer" title="{{WebUtility.HtmlEncode(title)}}" data-srcdoc="{{WebUtility.HtmlEncode(frame)}}"></iframe>
            <script>{{Asset("host.js")}}</script></body></html>
            """;
        if (Encoding.UTF8.GetByteCount(document) > 2_000_000) throw new IOException("The visualization is too large to preview.");
        return document;
    }
}
