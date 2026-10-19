using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace MiniCodex.Presentation;

internal sealed class SelectionAnnotation(FrameworkElement editor, Func<string> selectedText, Func<Rect> selectionBounds, Func<bool> canAnnotate)
{
    private Popup? _popup;
    private Window? _owner;
    private DispatcherOperation? _pending;
    private string _quote = "";

    public static void Attach(TextBoxBase editor, Func<string> selectedText, Func<Rect> selectionBounds, Func<bool>? canAnnotate = null)
    {
        var annotation = AttachCore(editor, selectedText, selectionBounds, canAnnotate);
        editor.SelectionChanged += annotation.SelectionChanged;
    }
    public static void Attach(MarkdownCodeView editor, Func<string> selectedText, Func<Rect> selectionBounds)
    {
        var annotation = AttachCore(editor, selectedText, selectionBounds, null);
        editor.TextArea.SelectionChanged += (_, _) => annotation.Schedule();
    }
    private static SelectionAnnotation AttachCore(FrameworkElement editor, Func<string> selectedText, Func<Rect> selectionBounds, Func<bool>? canAnnotate)
    {
        var annotation = new SelectionAnnotation(editor, selectedText, selectionBounds, canAnnotate ?? (() => true));
        editor.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(annotation.SelectionReleased), true);
        editor.PreviewKeyDown += annotation.KeyDown;
        editor.LostKeyboardFocus += (_, e) => { if (ReferenceEquals(e.OldFocus, editor)) annotation.Hide(); };
        editor.IsKeyboardFocusWithinChanged += (_, _) => { if (!editor.IsKeyboardFocusWithin) annotation.Hide(); };
        editor.IsVisibleChanged += (_, _) => { if (!editor.IsVisible) annotation.Hide(); };
        editor.Unloaded += (_, _) => annotation.Hide();
        editor.DataContextChanged += (_, _) => annotation.Hide();
        editor.ContextMenuOpening += (_, _) => annotation.Hide();
        return annotation;
    }
    private void SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed) Hide();
        else Schedule();
    }
    private void SelectionReleased(object sender, MouseButtonEventArgs e) => Schedule();
    private void Schedule()
    {
        _pending?.Abort();
        _pending = null;
        if (!editor.IsLoaded || !editor.IsVisible || !editor.IsKeyboardFocusWithin || !canAnnotate() || string.IsNullOrWhiteSpace(selectedText()))
        { Hide(); return; }
        _pending = editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(Show));
    }
    private void Show()
    {
        _pending = null;
        if (!editor.IsLoaded || !editor.IsVisible || !editor.IsKeyboardFocusWithin || !canAnnotate()
            || Mouse.LeftButton == MouseButtonState.Pressed || editor.ContextMenu?.IsOpen == true)
        { Hide(); return; }
        var quote = selectedText();
        var owner = Window.GetWindow(editor);
        if (string.IsNullOrWhiteSpace(quote) || owner is not { IsActive: true }) { Hide(); return; }
        Rect bounds;
        try { bounds = selectionBounds(); }
        catch (InvalidOperationException) { Hide(); return; }
        if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y)) { Hide(); return; }
        if (_popup is { IsOpen: true } && _quote == quote) return;
        if (_popup is null)
        {
            var button = new Button
            {
                Content = "Annotate", Style = (Style)Application.Current.FindResource("AccentButton"),
                FontSize = 11, Padding = new Thickness(9, 4, 9, 4), MinHeight = 27,
                Focusable = false, IsTabStop = false, ToolTip = "Attach the selected text to a comment"
            };
            AutomationProperties.SetName(button, "Annotate selected text");
            button.Click += (_, _) =>
            {
                var selected = _quote;
                Hide();
                if (canAnnotate() && !string.IsNullOrWhiteSpace(selected))
                    editor.RaiseEvent(new AnnotationEventArgs(MarkdownMessageView.AnnotationRequestedEvent, selected));
            };
            var surface = new Border { Child = button, Padding = new Thickness(2), CornerRadius = new CornerRadius(6) };
            surface.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            AppZoom.SetEnabled(surface, true);
            _popup = new Popup
            {
                PlacementTarget = editor, Placement = PlacementMode.Bottom, VerticalOffset = 4,
                StaysOpen = false, AllowsTransparency = true, Focusable = false, PopupAnimation = PopupAnimation.None, Child = surface
            };
            _popup.Closed += (_, _) => { _quote = ""; UnwatchOwner(); };
        }
        _quote = quote;
        _popup.PlacementRectangle = bounds;
        if (!ReferenceEquals(_owner, owner))
        {
            UnwatchOwner(); _owner = owner;
            owner.LocationChanged += OwnerChanged;
            owner.SizeChanged += OwnerResized;
            owner.Deactivated += OwnerChanged;
            owner.PreviewMouseWheel += OwnerWheel;
            owner.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OwnerScrolled), true);
        }
        _popup.IsOpen = true;
    }
    private void KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _popup?.IsOpen != true) return;
        Hide(); e.Handled = true;
    }
    private void OwnerChanged(object? sender, EventArgs e) => Hide();
    private void OwnerResized(object sender, SizeChangedEventArgs e) => Hide();
    private void OwnerWheel(object sender, MouseWheelEventArgs e) => Hide();
    private void OwnerScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.HorizontalChange != 0 || e.ViewportHeightChange != 0 || e.ViewportWidthChange != 0) Hide();
    }
    private void UnwatchOwner()
    {
        if (_owner is not { } owner) return;
        owner.LocationChanged -= OwnerChanged;
        owner.SizeChanged -= OwnerResized;
        owner.Deactivated -= OwnerChanged;
        owner.PreviewMouseWheel -= OwnerWheel;
        owner.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OwnerScrolled));
        _owner = null;
    }
    private void Hide()
    {
        _pending?.Abort(); _pending = null; _quote = "";
        if (_popup is not null) _popup.IsOpen = false;
        UnwatchOwner();
    }
}
