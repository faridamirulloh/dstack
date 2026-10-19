using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;

namespace MiniCodex.Presentation;

internal static class ImagePreview
{
    private sealed record Cached(long Length, DateTime Updated, WeakReference<BitmapSource> Image);
    private static readonly Dictionary<string, Cached> Thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);

    public static Inline Create(string path, string label) => CreatePreview(path, label, Thumbnail(path), null);
    public static Inline CreateRemote(Uri uri, string label) => CreatePreview(uri.AbsoluteUri, label, null, uri);

    private static Inline CreatePreview(string path, string label, BitmapSource? thumbnail, Uri? remote)
    {
        var name = FileLinks.Name(remote?.AbsolutePath ?? path);
        var caption = label.Trim().ReplaceLineEndings(" ");
        if (caption.Length == 0 || caption.Equals(name, StringComparison.OrdinalIgnoreCase)
            || caption.Equals(path, StringComparison.OrdinalIgnoreCase))
            caption = remote is null ? "Generated image" : "Image";
        var content = new StackPanel { MaxWidth = 360 };
        var image = new Image { Source = thumbnail, MaxWidth = 360, MaxHeight = 300, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Visibility = thumbnail is null ? Visibility.Collapsed : Visibility.Visible };
        var status = new TextBlock { Text = remote is null ? "Image preview unavailable" : "Loading image…", Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 6, 0, 6), MinHeight = remote is null ? 0 : 70, TextWrapping = TextWrapping.Wrap, Visibility = thumbnail is null ? Visibility.Visible : Visibility.Collapsed };
        content.Children.Add(image); content.Children.Add(status);
        content.Children.Add(new TextBlock { Text = caption, FontSize = 11, Foreground = Brush("FileLinkBrush"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) });
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("QuietButton"), Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = caption + "\n" + path + "\nClick to open the full image." + (remote is null ? "\nCtrl+click to show in Explorer.\nDrag to copy the original image file." : ""),
            Content = new Border { Child = content, Padding = new Thickness(8), CornerRadius = new CornerRadius(6), Background = Brush("CodeSurfaceBrush"), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1) }
        };
        AutomationProperties.SetName(button, "Open image: " + caption);
        if (remote is null) FileLinks.EnableDrag(button, () => path);
        void Open() { if (remote is null) FileLinks.Open(path); else OpenRemote(remote); }
        button.Click += (_, e) =>
        {
            e.Handled = true;
            if (remote is null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) FileLinks.ShowInExplorer(path);
            else Open();
        };
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open full image" }; open.Click += (_, _) => Open();
        menu.Items.Add(open);
        if (remote is null)
        {
            var explore = new MenuItem { Header = "Show in Explorer" }; explore.Click += (_, _) => FileLinks.ShowInExplorer(path);
            menu.Items.Add(explore);
        }
        button.ContextMenu = menu;
        var container = new PreviewGrid { Margin = new Thickness(0, 5, 0, 8) };
        var preview = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        container.Children.Add(preview);
        preview.Children.Add(button);
        var annotateStyle = new Style(typeof(Button), (Style)Application.Current.FindResource("AccentButton"));
        annotateStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Hidden));
        foreach (var property in new[] { nameof(UIElement.IsMouseOver), nameof(UIElement.IsKeyboardFocusWithin) })
        {
            var show = new DataTrigger { Binding = new Binding(property) { Source = preview }, Value = true };
            show.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
            annotateStyle.Triggers.Add(show);
        }
        var annotate = new Button
        {
            Content = "Annotate", Style = annotateStyle, FontSize = 11, MinHeight = 27,
            Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Add this image and a comment to your next message"
        };
        AutomationProperties.SetName(annotate, "Annotate image: " + caption);
        annotate.Click += (_, e) =>
        {
            e.Handled = true;
            annotate.RaiseEvent(new AnnotationEventArgs(MarkdownMessageView.AnnotationRequestedEvent,
                "Image: " + caption + "\n" + path, remote is null ? path : null));
        };
        preview.Children.Add(annotate);
        if (remote is not null)
        {
            var loading = false;
            async void Load()
            {
                if (loading || image.Source is not null) return;
                loading = true; status.Text = "Loading image…";
                try
                {
                    var downloaded = await RemoteThumbnail(remote);
                    if (downloaded is not null)
                    { image.Source = downloaded; image.Visibility = Visibility.Visible; status.Visibility = Visibility.Collapsed; }
                    else status.Text = "Image preview unavailable. Click to open the image.";
                }
                finally { loading = false; }
            }
            preview.Loaded += (_, _) => Load();
            var retry = new MenuItem { Header = "Retry preview" };
            retry.Click += (_, _) => { RemoteImages.TryRemove(path, out _); Load(); };
            menu.Items.Add(retry);
        }
        return new InlineUIContainer(container) { BaselineAlignment = BaselineAlignment.Center };
    }

    private sealed class PreviewGrid : Grid
    {
        protected override Size MeasureOverride(Size constraint)
        {
            var desired = base.MeasureOverride(constraint);
            // Inline containers use the child's desired width instead of stretching it.
            return new Size(double.IsFinite(constraint.Width) ? constraint.Width : desired.Width, desired.Height);
        }
    }

    private sealed record RemoteCached(DateTimeOffset Attempted, WeakReference<BitmapSource>? Image);
    private static readonly ConcurrentDictionary<string, RemoteCached> RemoteImages = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> Downloads = new();
    private static readonly SemaphoreSlim DownloadGate = new(3);
    private static readonly HttpClient ImageClient = new(new HttpClientHandler { UseCookies = false, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(20) };
    private const int MaximumBytes = 25 * 1024 * 1024;

    private static async Task<BitmapSource?> RemoteThumbnail(Uri uri)
    {
        var key = uri.AbsoluteUri;
        if (RemoteImages.TryGetValue(key, out var cached))
        {
            if (cached.Image?.TryGetTarget(out var image) == true) return image;
            if (cached.Image is null && DateTimeOffset.UtcNow - cached.Attempted < TimeSpan.FromMinutes(1)) return null;
        }
        var work = Downloads.GetOrAdd(key, _ => new Lazy<Task<BitmapSource?>>(() => Download(uri)));
        try
        {
            var image = await work.Value;
            if (RemoteImages.Count >= 128)
                foreach (var item in RemoteImages.Keys.Take(RemoteImages.Count - 127)) RemoteImages.TryRemove(item, out _);
            RemoteImages[key] = new(DateTimeOffset.UtcNow, image is null ? null : new(image)); return image;
        }
        finally { Downloads.TryRemove(key, out _); }
    }
    private static async Task<BitmapSource?> Download(Uri uri)
    {
        await DownloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var response = await ImageClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength.GetValueOrDefault() > MaximumBytes) return null;
            using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var data = new MemoryStream(); var buffer = new byte[16 * 1024];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (await body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false) is var count && count > 0)
            {
                if (data.Length + count > MaximumBytes) return null;
                data.Write(buffer, 0, count);
            }
            data.Position = 0;
            return await Task.Run(() => Decode(data)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or FormatException)
        { return null; }
        finally { DownloadGate.Release(); }
    }
    private static BitmapSource? Decode(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0]; var width = frame.PixelWidth; var height = frame.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > 40_000_000) return null;
        stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
        if (width >= height) image.DecodePixelWidth = Math.Min(width, 480); else image.DecodePixelHeight = Math.Min(height, 480);
        image.EndInit(); image.Freeze(); return image;
    }
    private static void OpenRemote(Uri uri)
    {
        if (uri.Scheme is not ("https" or "http")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { AppDialog.Show(error.Message, "Could not open image"); }
    }

    private static BitmapSource? Thumbnail(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 25 * 1024 * 1024) return null;
            if (Thumbnails.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Updated == info.LastWriteTimeUtc && cached.Image.TryGetTarget(out var existing)) return existing;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var width = frame.PixelWidth; var height = frame.PixelHeight;
            if (width <= 0 || height <= 0 || (long)width * height > 40_000_000) return null;
            stream.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
            if (width >= height) image.DecodePixelWidth = Math.Min(width, 480);
            else image.DecodePixelHeight = Math.Min(height, 480);
            image.EndInit(); image.Freeze();
            if (Thumbnails.Count >= 128)
                foreach (var key in Thumbnails.Where(pair => !pair.Value.Image.TryGetTarget(out _)).Select(pair => pair.Key).ToArray()) Thumbnails.Remove(key);
            if (Thumbnails.Count >= 128) Thumbnails.Remove(Thumbnails.Keys.First());
            Thumbnails[path] = new(info.Length, info.LastWriteTimeUtc, new(image));
            return image;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or FormatException)
        { return null; }
    }
}
