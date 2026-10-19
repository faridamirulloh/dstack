using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mermaider;
using SkiaSharp;
using Svg.Skia;
using WpfMath.Controls;

namespace MiniCodex.Presentation;

internal static class MarkdownVisuals
{
    private static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);

    internal static FrameworkElement Formula(string source, bool display) => new FormulaView(source, display);
    internal static Block Diagram(string source) => new BlockUIContainer(new DiagramView(source)) { Margin = new Thickness(0, 6, 0, 12) };

    private sealed class FormulaView : Border, IMarkdownSearchContent
    {
        private readonly Expander _source;
        private readonly MarkdownCodeView _editor;
        public string SourceText { get; }
        public FormulaView(string source, bool display)
        {
            SourceText = source;
            var formula = new FormulaControl { Scale = display ? 20 : 16, Foreground = Brush("TextBrush"), Formula = source };
            var panel = new StackPanel();
            if (!formula.HasError)
                panel.Children.Add(new Viewbox { Child = formula, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = display ? 680 : 480 });
            else
                panel.Children.Add(new TextBlock { Text = source, Foreground = Brush("AccentBrush"), FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap });
            _editor = new MarkdownCodeView(source, "tex", true);
            _source = new Expander { Header = "LaTeX source", Content = _editor, Style = (Style)Application.Current.FindResource("TerminalExpander"), Foreground = Brush("MutedBrush"), FontSize = 11, IsExpanded = formula.HasError };
            if (display) panel.Children.Add(_source);
            Child = panel; Padding = new Thickness(display ? 12 : 3, display ? 9 : 1, display ? 12 : 3, display ? 9 : 1);
            if (display) { Background = Brush("CodeSurfaceBrush"); CornerRadius = new CornerRadius(5); }
            ToolTip = source;
            AutomationProperties.SetName(this, "Formula: " + source);
            var copy = new MenuItem { Header = "Copy LaTeX" }; copy.Click += (_, _) => MarkdownCodeView.CopyText(source);
            var annotate = new MenuItem { Header = "Annotate formula" };
            annotate.Click += (_, _) => RaiseEvent(new AnnotationEventArgs(MarkdownMessageView.AnnotationRequestedEvent, (display ? "$$\n" : "$") + source + (display ? "\n$$" : "$")));
            ContextMenu = new ContextMenu { Items = { copy, annotate } };
        }
        public void SelectMatch(int offset, int length)
        {
            ChatMessageSurface.AllowNavigation(this);
            if (_source.Parent is null && Child is StackPanel panel) panel.Children.Add(_source);
            _source.IsExpanded = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _editor.SelectMatch(offset, length)));
            BringIntoView();
        }
    }

    private sealed class DiagramView : Border, IMarkdownSearchContent
    {
        private readonly Image _image = new() { Stretch = Stretch.Uniform, MaxWidth = 680, MaxHeight = 520, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _status = new() { Text = "Rendering diagram…", Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap };
        private readonly Expander _source;
        private readonly MarkdownCodeView _editor;
        private bool _loading;
        public string SourceText { get; }
        public DiagramView(string source)
        {
            SourceText = source;
            var panel = new DockPanel();
            var header = new DockPanel { Margin = new Thickness(10, 6, 8, 6) };
            var copy = MarkdownCodeView.CopyButton(source, "Copy Mermaid source"); DockPanel.SetDock(copy, Dock.Right); header.Children.Add(copy);
            header.Children.Add(new TextBlock { Text = "Mermaid", Foreground = Brush("CodeLabelBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
            _editor = new MarkdownCodeView(source, "mermaid", true);
            _source = new Expander { Header = "Diagram source", Content = _editor, Style = (Style)Application.Current.FindResource("TerminalExpander"), Foreground = Brush("MutedBrush"), FontSize = 11, Margin = new Thickness(10, 4, 10, 6) };
            DockPanel.SetDock(_source, Dock.Bottom); panel.Children.Add(_source);
            var preview = new StackPanel { Margin = new Thickness(8) }; preview.Children.Add(_status); preview.Children.Add(_image); panel.Children.Add(preview);
            Child = panel; Background = Brush("CodeSurfaceBrush"); BorderBrush = Brush("LineBrush"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(5);
            _status.Foreground = Brush("MutedBrush");
            AutomationProperties.SetName(_image, "Mermaid diagram");
            var annotate = new MenuItem { Header = "Annotate diagram" };
            annotate.Click += (_, _) => RaiseEvent(new AnnotationEventArgs(MarkdownMessageView.AnnotationRequestedEvent, "```mermaid\n" + source + "\n```"));
            var copyItem = new MenuItem { Header = "Copy Mermaid source" }; copyItem.Click += (_, _) => MarkdownCodeView.CopyText(source);
            ContextMenu = new ContextMenu { Items = { copyItem, annotate } };
            Loaded += LoadDiagram;
        }
        private async void LoadDiagram(object sender, RoutedEventArgs e)
        {
            if (_loading || _image.Source is not null) return;
            _loading = true;
            try
            {
                _image.Source = await DiagramImage(SourceText);
                _status.Visibility = Visibility.Collapsed;
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
            {
                _status.Text = "Diagram could not be rendered. The Mermaid source is available below.";
                _status.ToolTip = error.Message; _source.IsExpanded = true;
            }
            finally { _loading = false; }
        }
        public void SelectMatch(int offset, int length)
        {
            ChatMessageSurface.AllowNavigation(this);
            _source.IsExpanded = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _editor.SelectMatch(offset, length)));
            BringIntoView();
        }
    }

    private static readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> Diagrams = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource>>> Pending = new();
    private static readonly SemaphoreSlim RenderGate = new(1);
    private static async Task<BitmapSource> DiagramImage(string source)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        if (Diagrams.TryGetValue(key, out var cached) && cached.TryGetTarget(out var image)) return image;
        var work = Pending.GetOrAdd(key, _ => new Lazy<Task<BitmapSource>>(() => RenderDiagram(source)));
        try
        {
            image = await work.Value;
            if (Diagrams.Count >= 32)
                foreach (var item in Diagrams.Keys.Take(Diagrams.Count - 31)) Diagrams.TryRemove(item, out _);
            Diagrams[key] = new(image); return image;
        }
        finally { Pending.TryRemove(key, out _); }
    }
    private static async Task<BitmapSource> RenderDiagram(string source)
    {
        await RenderGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var xml = MermaidRenderer.RenderSvg(source, new Mermaider.Models.RenderOptions
                {
                    Bg = "#181D25", Fg = "#E9EAED", Accent = "#B2D2FF", Muted = "#AFB2BD",
                    Surface = "#222429", Border = "#565B66", Line = "#AFB2BD", Font = "Segoe UI", MonoFont = "Consolas"
                });
                using var svg = new SKSvg();
                var picture = svg.FromSvg(xml) ?? throw new InvalidDataException("Mermaid produced an empty diagram.");
                var bounds = picture.CullRect;
                if (!float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
                    throw new InvalidDataException("The diagram has invalid dimensions.");
                var scale = Math.Min(2f, Math.Min(1800f / bounds.Width, 1600f / bounds.Height));
                var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
                var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
                using var surface = SKSurface.Create(new SKImageInfo(width, height)) ?? throw new InvalidDataException("The diagram image could not be allocated.");
                surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale(scale); surface.Canvas.Translate(-bounds.Left, -bounds.Top); surface.Canvas.DrawPicture(picture);
                using var snapshot = surface.Snapshot(); using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100); using var stream = data.AsStream();
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                return (BitmapSource)bitmap;
            }).ConfigureAwait(false);
        }
        finally { RenderGate.Release(); }
    }
}
