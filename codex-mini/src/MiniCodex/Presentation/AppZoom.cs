using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MiniCodex.Services;

namespace MiniCodex.Presentation;

public sealed class AppZoom : ObservableObject
{
    public static AppZoom Current { get; } = new();
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(AppZoom), new PropertyMetadata(false, EnabledChanged));
    private AppController? _controller;
    private double _scale = 1;
    public double Scale => _scale;

    public void Initialize(AppController controller)
    {
        _controller = controller;
        UpdateScale();
        controller.Changed += (_, _) => UpdateScale();
    }
    private void UpdateScale()
    {
        var scale = _controller?.State.UiZoom ?? 1;
        if (_scale == scale) return;
        _scale = scale; Changed(nameof(Scale));
    }
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    private static void EnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            element.Loaded += Loaded; element.PreviewKeyDown += KeyDown;
            if (element.IsLoaded) Apply(element);
        }
        else { element.Loaded -= Loaded; element.PreviewKeyDown -= KeyDown; }
    }
    private static void Loaded(object sender, RoutedEventArgs e) => Apply((FrameworkElement)sender);
    private static void Apply(FrameworkElement element)
    {
        var root = element is Window window ? window.Content as FrameworkElement : element;
        if (root is null) return;
        var transform = new ScaleTransform();
        BindingOperations.SetBinding(transform, ScaleTransform.ScaleXProperty, new Binding(nameof(Scale)) { Source = Current });
        BindingOperations.SetBinding(transform, ScaleTransform.ScaleYProperty, new Binding(nameof(Scale)) { Source = Current });
        root.LayoutTransform = transform;
    }
    private static void KeyDown(object sender, KeyEventArgs e)
    {
        var controller = Current._controller;
        if (e.Handled || controller is null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) return;
        var step = e.Key switch { Key.OemPlus or Key.Add => 0.05, Key.OemMinus or Key.Subtract => -0.05, _ => 0 };
        if (step == 0) return;
        e.Handled = true;
        var zoom = Math.Clamp(Math.Round(Current.Scale + step, 2), 0.75, 1.5);
        if (zoom == Current.Scale) return;
        try { controller.Commit(state => state.UiZoom = zoom); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { AppDialog.Show(error.Message, "Could not save zoom"); }
    }
}
