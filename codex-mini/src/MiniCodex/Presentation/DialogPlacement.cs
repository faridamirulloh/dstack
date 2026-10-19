using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MiniCodex.Presentation;

public static class DialogPlacement
{
    public static void CenterOn(Window dialog, FrameworkElement? anchor)
    {
        if (anchor is null) return;
        var bounds = VisibleBounds(anchor, out var viewport);
        if (!bounds.IsEmpty) dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        if (dialog.IsLoaded)
        {
            Position(dialog, anchor);
            dialog.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Position(dialog, anchor)));
            return;
        }

        if (!bounds.IsEmpty && PresentationSource.FromVisual(anchor)?.CompositionTarget is { } source)
        {
            var toDips = new MatrixTransform(source.TransformFromDevice);
            var target = toDips.TransformBounds(bounds);
            if (double.IsFinite(dialog.Width) && dialog.SizeToContent is SizeToContent.Manual or SizeToContent.Height)
                dialog.Width = Math.Min(dialog.Width, Math.Max(dialog.MinWidth, target.Width - 16));
            SetPosition(dialog, target, toDips.TransformBounds(viewport),
                double.IsFinite(dialog.Width) ? dialog.Width : dialog.MinWidth,
                double.IsFinite(dialog.Height) ? dialog.Height : dialog.MinHeight);
        }
        dialog.Loaded += Loaded;
        dialog.ContentRendered += Rendered;

        void Loaded(object sender, RoutedEventArgs e)
        {
            dialog.Loaded -= Loaded;
            Position(dialog, anchor);
        }
        void Rendered(object? sender, EventArgs e)
        {
            dialog.ContentRendered -= Rendered;
            Position(dialog, anchor);
        }
    }

    private static void Position(Window dialog, FrameworkElement anchor)
    {
        if (dialog.WindowState != WindowState.Normal || !dialog.IsVisible
            || PresentationSource.FromVisual(dialog)?.CompositionTarget is not { } source) return;
        var bounds = VisibleBounds(anchor, out var viewport);
        if (bounds.IsEmpty) return;
        var toDips = new MatrixTransform(source.TransformFromDevice);
        SetPosition(dialog, toDips.TransformBounds(bounds), toDips.TransformBounds(viewport), dialog.ActualWidth, dialog.ActualHeight);
    }

    private static void SetPosition(Window dialog, Rect target, Rect limits, double width, double height)
    {
        dialog.Left = Math.Clamp(target.Left + (target.Width - width) / 2,
            limits.Left, Math.Max(limits.Left, limits.Right - width));
        dialog.Top = Math.Clamp(target.Top + (target.Height - height) / 2,
            limits.Top, Math.Max(limits.Top, limits.Bottom - height));
    }

    private static Rect VisibleBounds(FrameworkElement anchor, out Rect viewport)
    {
        viewport = Rect.Empty;
        if (!anchor.IsVisible || anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0
            || PresentationSource.FromVisual(anchor) is null || Window.GetWindow(anchor)?.Content is not FrameworkElement root) return Rect.Empty;
        viewport = ScreenBounds(root);
        var bounds = ScreenBounds(anchor);
        bounds.Intersect(viewport);
        return bounds;
    }

    private static Rect ScreenBounds(FrameworkElement element) => new(
        element.PointToScreen(new Point()), element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));
}
