using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MiniCodex.Presentation;
using MiniCodex.ViewModels;

namespace MiniCodex.Views;

public partial class AccountPane
{
    private readonly DispatcherTimer _thinkingTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly Geometry CompletedTaskIcon = Geometry.Parse("M 2,7 L 5,10 L 12,3");
    private static readonly Geometry StoppedTaskIcon = Geometry.Parse("M 2,2 L 12,2 L 12,12 L 2,12 Z");
    private static readonly Geometry WaitingTaskIcon = Geometry.Parse("M 5,2 L 5,12 M 9,2 L 9,12");
    private static readonly Geometry AttentionTaskIcon = Geometry.Parse("M 7,1 A 6,6 0 1 1 7,13 A 6,6 0 1 1 7,1 M 7,4 L 7,7 M 7,9.5 L 7,9.6");

    private void InitializeActivity()
    {
        _thinkingTimer.Tick += ThinkingTick;
        _model.PropertyChanged += ThinkingModelChanged;
        IsVisibleChanged += ActivityVisibilityChanged;
        Loaded += ActivityLoaded;
    }
    private void DisposeActivity()
    {
        _thinkingTimer.Stop(); _thinkingTimer.Tick -= ThinkingTick;
        _model.PropertyChanged -= ThinkingModelChanged;
        IsVisibleChanged -= ActivityVisibilityChanged;
        Loaded -= ActivityLoaded;
        if (_hostWindow is not null) TrackHostActivity(_hostWindow, false);
        Runtime.SetViewedConversation(this, null);
    }
    private void ThinkingTick(object? sender, EventArgs e) => UpdateThinking();
    private void ActivityVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { UpdateViewedConversation(); UpdateThinking(); }
    private void ActivityLoaded(object sender, RoutedEventArgs e) { UpdateViewedConversation(); UpdateThinking(); }
    private void ActivityWindowChanged(object? sender, EventArgs e) => UpdateViewedConversation();
    private void TrackHostActivity(AccountWindow window, bool attach)
    {
        if (attach)
        {
            window.Activated += ActivityWindowChanged;
            window.Deactivated += ActivityWindowChanged;
            window.StateChanged += ActivityWindowChanged;
            window.ViewportChanged += ActivityWindowChanged;
        }
        else
        {
            window.Activated -= ActivityWindowChanged;
            window.Deactivated -= ActivityWindowChanged;
            window.StateChanged -= ActivityWindowChanged;
            window.ViewportChanged -= ActivityWindowChanged;
        }
    }
    private void UpdateViewedConversation() => Runtime.SetViewedConversation(this,
        IsLoaded && IsVisible && _hostWindow is { IsActive: true } host && host.WindowState != WindowState.Minimized
            && host.IsPaneInView(this) && !_closing ? _model.SelectedChat?.Id : null);
    private void ThinkingModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.SelectedChat)) UpdateViewedConversation();
        if (e.PropertyName == nameof(AccountViewModel.SelectedChat)
            || e.PropertyName == nameof(AccountViewModel.ChatStatus) && (!_thinkingTimer.IsEnabled || _model.Conversation is not { Busy: true, Uncertain: false, TaskFinishedAt: null })) UpdateThinking();
    }
    private void UpdateThinking()
    {
        if (!IsLoaded || !IsVisible || _closing || _model.Conversation is not { } state || _model.ChatStatus is "Ready" or "Loading history")
        {
            _thinkingTimer.Stop(); TaskProgressIndicator.Visibility = Visibility.Collapsed;
            return;
        }
        var active = state.Busy && !state.Uncertain && state.TaskFinishedAt is null;
        if (active) { if (!_thinkingTimer.IsEnabled) _thinkingTimer.Start(); }
        else _thinkingTimer.Stop();
        var activity = _model.ChatStatus;
        var waiting = activity is "Waiting for approval" or "Waiting for your answer";
        if (active && activity == "Thinking" && Descendants(Transcript).OfType<MarkdownMessageView>().Any(view => view.IsRevealing)) activity = "Writing response";
        TaskProgressIndicator.Visibility = Visibility.Visible;
        TaskActivitySpinner.Visibility = active && !waiting ? Visibility.Visible : Visibility.Collapsed;
        TaskStateIcon.Visibility = active && !waiting ? Visibility.Collapsed : Visibility.Visible;
        TaskStateIcon.Data = activity switch
        {
            "Completed" => CompletedTaskIcon,
            "Stopped" => StoppedTaskIcon,
            "Failed" or "Outcome unknown" => AttentionTaskIcon,
            _ => WaitingTaskIcon
        };
        var brush = (Brush)FindResource(activity switch
        {
            "Completed" => "GoodBrush",
            "Failed" => "DangerBrush",
            "Waiting for approval" or "Waiting for your answer" or "Outcome unknown" => "WarningBrush",
            "Paused" or "Stopping for conflict" => "PausedTaskBrush",
            _ => "MutedBrush"
        });
        TaskStateIcon.Stroke = brush; ThinkingText.Foreground = brush;
        ThinkingText.Visibility = Visibility.Visible;
        TaskElapsedText.Visibility = state.TaskStartedAt is not null && !state.Uncertain ? Visibility.Visible : Visibility.Collapsed;
        var end = active ? DateTimeOffset.UtcNow : state.TaskFinishedAt ?? state.TaskStartedAt ?? DateTimeOffset.UtcNow;
        var elapsed = end - (state.TaskStartedAt ?? end);
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var duration = elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        TaskElapsedText.Text = duration;
        var dots = (Environment.TickCount64 / 500 % 3) switch { 0 => ".", 1 => ". .", _ => ". . ." };
        ThinkingText.Text = activity + (active && !waiting ? " " + dots : "");
    }
}
