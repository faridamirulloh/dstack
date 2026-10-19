using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MiniCodex.Presentation;

internal static class CopyFeedback
{
    private static readonly ConditionalWeakTable<Button, FeedbackState> States = new();

    internal static void Show(Button button) => States.GetValue(button, owner => new FeedbackState(owner)).Show();
    internal static void Reset(Button button)
    { if (States.TryGetValue(button, out var state)) state.Reset(); }

    private sealed class FeedbackState
    {
        private readonly Button _button;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
        private DataTemplate? _originalTemplate;
        private object? _originalToolTip;
        private bool _showing;

        internal FeedbackState(Button button)
        {
            _button = button;
            _timer.Tick += (_, _) => Reset();
            button.Unloaded += (_, _) => Reset();
            button.DataContextChanged += (_, _) => Reset();
        }

        internal void Show()
        {
            if (!_showing)
            {
                _originalTemplate = _button.ContentTemplate;
                _originalToolTip = _button.ToolTip;
            }
            _showing = true;
            _button.SetCurrentValue(ContentControl.ContentTemplateProperty, (DataTemplate)_button.FindResource("CopiedIconTemplate"));
            _button.SetCurrentValue(FrameworkElement.ToolTipProperty, "Copied");
            _timer.Stop(); _timer.Start();
        }

        internal void Reset()
        {
            _timer.Stop();
            if (!_showing) return;
            _showing = false;
            _button.SetCurrentValue(ContentControl.ContentTemplateProperty, _originalTemplate);
            _button.SetCurrentValue(FrameworkElement.ToolTipProperty, _originalToolTip);
            _originalTemplate = null; _originalToolTip = null;
        }
    }
}
