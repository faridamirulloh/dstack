using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MiniCodex.Presentation;

public sealed class ChatMessageSurface : Grid
{
    private bool _preserveFocusScroll;
    private int _focusVersion;

    public ChatMessageSurface()
    {
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(PointerDown), true);
        RequestBringIntoView += (_, e) => { if (_preserveFocusScroll) e.Handled = true; };
        ContextMenuClosing += (_, _) => PreserveFocusScroll();
        AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => ResetFocusScroll()), true);
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) => ResetFocusScroll()), true);
        Unloaded += (_, _) => ResetFocusScroll();
        DataContextChanged += (_, _) => ResetFocusScroll();
    }

    private void PointerDown(object sender, MouseButtonEventArgs e)
    {
        ResetFocusScroll();
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null && !ReferenceEquals(current, this); current = ParentOf(current))
            if (current is ScrollBar) return;
        PreserveFocusScroll();
    }

    private void PreserveFocusScroll()
    {
        _preserveFocusScroll = true;
        var version = ++_focusVersion;
        // Stop mouse focus and menu focus restoration from scrolling a whole message
        // into view. Let nested controls handle their own scrolling first.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        { if (version == _focusVersion) _preserveFocusScroll = false; }));
    }

    private void ResetFocusScroll()
    {
        _preserveFocusScroll = false;
        _focusVersion++;
    }

    internal static void AllowNavigation(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = ParentOf(current))
            if (current is ChatMessageSurface surface) surface.ResetFocusScroll();
    }

    private static DependencyObject? ParentOf(DependencyObject value) => value switch
    {
        FrameworkContentElement content => content.Parent,
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(value),
        _ => LogicalTreeHelper.GetParent(value)
    };
}
