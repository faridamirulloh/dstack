using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MiniCodex.Presentation;

internal static partial class Program
{
    private static void RunVisualizationChecks()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MiniCodex;component/Presentation/Theme.xaml", UriKind.Relative) });
        var root = Path.GetFullPath("..");
        var path = Path.Combine(root, "work", "chat-action-icon-options.html");
        var message = new MarkdownMessageView
        {
            Role = "Codex", BaseDirectory = root,
            Text = "\uE200visualize\uE202" + JsonSerializer.Serialize(new { path, title = "Icon choices" }) + "\uE201"
        };
        var content = new StackPanel();
        content.Children.Add(message);
        content.Children.Add(new Border { Height = 900 });
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window
        {
            Content = scroll, Width = 900, Height = 500,
            ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000
        };
        var frames = new List<CoreWebView2Frame>();
        try
        {
            window.Show();
            UIElement? Preview() => message.Document.Blocks.OfType<Paragraph>()
                .SelectMany(block => block.Inlines.OfType<InlineUIContainer>()).Select(inline => inline.Child).FirstOrDefault();
            WaitUntil(() => Preview() is not null, "Chat markdown creates its HTML preview");
            var preview = Preview()!;
            var browserField = preview.GetType().GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!;
            WaitUntil(() => browserField.GetValue(preview) is WebView2, "The preview creates its windowed browser");
            var browser = (WebView2)browserField.GetValue(preview)!;
            void ObserveBrowser(WebView2 view)
            {
                void ObserveCore()
                {
                    var core = view.CoreWebView2!;
                    core.FrameCreated += (_, e) =>
                    {
                        frames.Add(e.Frame);
                        e.Frame.NavigationStarting += (_, args) => Console.WriteLine("FRAME NAV: " + args.Uri[..Math.Min(args.Uri.Length, 100)]);
                        e.Frame.NavigationCompleted += (_, args) => Console.WriteLine("FRAME RESULT: " + args.IsSuccess + " " + args.WebErrorStatus);
                    };
                    core.NavigationStarting += (_, e) => Console.WriteLine("HOST NAV: " + e.Uri[..Math.Min(e.Uri.Length, 100)]);
                    core.NavigationCompleted += (_, e) => Console.WriteLine("HOST RESULT: " + e.IsSuccess + " " + e.WebErrorStatus);
                    core.GetDevToolsProtocolEventReceiver("Log.entryAdded").DevToolsProtocolEventReceived += (_, e) => Console.WriteLine("BROWSER LOG: " + e.ParameterObjectAsJson[..Math.Min(e.ParameterObjectAsJson.Length, 2000)]);
                    core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, e) => Console.WriteLine("BROWSER ERROR: " + e.ParameterObjectAsJson[..Math.Min(e.ParameterObjectAsJson.Length, 2000)]);
                    _ = core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
                    _ = core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                }
                if (view.CoreWebView2 is not null) ObserveCore();
                else view.CoreWebView2InitializationCompleted += (_, e) => { if (e.IsSuccess) ObserveCore(); };
            }
            ObserveBrowser(browser);
            WaitUntil(() => browser.CoreWebView2 is not null, "The preview browser initializes");
            Pump(2000);
            var status = (TextBlock)preview.GetType().GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            Console.WriteLine("PREVIEW STATUS: " + status.Text + "; loaded=" + ((FrameworkElement)preview).IsLoaded + "; visible=" + preview.IsVisible);
            Expect(ReferenceEquals(browserField.GetValue(preview), browser), "The preview browser remains active: " + status.Text);
            Console.WriteLine("HOST DOM: " + Await(browser.ExecuteScriptAsync("({url:location.href.slice(0,100),ready:document.readyState,text:document.body.innerText.slice(0,200),frame:!!document.querySelector('iframe'),srcdocLength:document.querySelector('iframe')?.srcdoc.length,bridge:typeof window.chrome?.webview,background:getComputedStyle(document.body).backgroundColor})"), "The host document can be inspected"));
            Console.WriteLine("NATIVE CLIP: " + preview.GetType().GetField("_browserClip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview));
            var capturePath = Path.Combine(AppContext.BaseDirectory, "visualization-preview.png");
            using (var capture = File.Create(capturePath))
            {
                var task = browser.CoreWebView2!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
                WaitUntil(() => task.IsCompleted, "The browser preview can be captured");
                task.GetAwaiter().GetResult();
            }
            Console.WriteLine("CAPTURE: " + capturePath);
            Expect(frames.Count == 1, "The host creates one sandboxed visualization frame");
            var frameDom = Await(frames[0].ExecuteScriptAsync("({url:location.href,ready:document.readyState,title:document.querySelector('h3')?.textContent,buttons:document.querySelectorAll('#mini-codex-icon-options [data-choice]').length,bridge:typeof window.openai,text:document.body.innerText.slice(0,400)})"), "The sandboxed visualization can be inspected");
            Console.WriteLine("FRAME DOM: " + frameDom);
            using var json = JsonDocument.Parse(frameDom);
            Equal(6, json.RootElement.GetProperty("buttons").GetInt32(), "Icon choices appear inside the HTML preview");
            Equal("object", json.RootElement.GetProperty("bridge").GetString(), "The visualization bridge initializes");

            var stateWritten = false;
            browser.CoreWebView2!.WebMessageReceived += (_, e) =>
            {
                using var received = JsonDocument.Parse(e.WebMessageAsJson);
                stateWritten |= received.RootElement.GetProperty("type").GetString() == "widget-state-write";
            };
            Equal("false", Await(frames[0].ExecuteScriptAsync("document.querySelector('[data-group=review][data-choice=inspect]').click(); document.querySelector('[data-group=order][data-choice=newest]').click(); document.querySelector('#mini-use-icons').disabled"), "Choices can be selected"), "Selecting both icons enables the apply button");
            WaitUntil(() => stateWritten, "The host receives widget state through its native bridge");

            scroll.ScrollToVerticalOffset(100); Pump(100);
            Expect(ReferenceEquals(browserField.GetValue(preview), browser), "Scrolling preserves the loaded preview");
            var clipping = ((IntPtr Window, Int32Rect Bounds))preview.GetType().GetField("_browserClip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            Expect(clipping.Bounds.Y > 0 && clipping.Bounds.Height < browser.ActualHeight, "The native browser is clipped to the scrolled chat viewport");
            scroll.ScrollToTop(); Pump(100);

            var reload = VisualizationDescendants(preview).OfType<Button>().Single(button => Equals(button.Content, "Reload"));
            frames.Clear();
            reload.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => browserField.GetValue(preview) is WebView2 current && !ReferenceEquals(current, browser), "Reload replaces the browser");
            browser = (WebView2)browserField.GetValue(preview)!;
            ObserveBrowser(browser);
            WaitUntil(() => frames.Count == 1, "Reload creates its sandboxed frame");
            WaitUntil(() => Await(frames[0].ExecuteScriptAsync("document.querySelectorAll('[data-choice][aria-pressed=true]').length"), "Restored choices can be inspected") == "2", "Reload restores both selected icon choices");
            Expect(ReferenceEquals(browserField.GetValue(preview), browser), "Reloaded content survives chat layout");

            var rejected = false;
            browser.CoreWebView2!.NavigationStarting += (_, e) => rejected = e.Cancel;
            browser.CoreWebView2.Navigate("data:text/html,%3Cp%3Eunexpected%3C/p%3E");
            WaitUntil(() => rejected, "The host rejects a different data URL after its initial document");
            Equal("true", Await(browser.ExecuteScriptAsync("!!document.querySelector('#mini-visualization')"), "The trusted host remains loaded"), "Blocked navigation preserves the visualization host");

            window.Content = null; Pump(100);
            Expect(browserField.GetValue(preview) is null, "Leaving the chat releases the browser after layout settles");
            Console.WriteLine($"PASS: {_checks} visualization checks.");
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    }

    private static IEnumerable<DependencyObject> VisualizationDescendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualizationDescendants(child)) yield return descendant;
        }
    }
}
