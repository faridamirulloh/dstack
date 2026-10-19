using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.ViewModels;
using MiniCodex.Codex;

namespace MiniCodex.Views;

public partial class AccountPane
{
    private bool _followTail = true;
    private bool _tailScrollQueued;
    private bool _manualScrolling;
    private bool _scrollBarInteraction;
    private readonly DispatcherTimer _manualScrollTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(160) };
    private bool IsTranscriptAnimating => Descendants(Transcript).OfType<MarkdownMessageView>().Any(view => view.IsRevealing);
    private const double TranscriptTopTolerance = 1;
    private const double TranscriptBottomTolerance = 8;
    private Guid? _displayedChat;
    private string? _userMessageAnchor;
    private bool _loadingPreviousUserMessage;
    private bool _preservingHistory;
    private int _historyRestoreVersion;
    private sealed record ScrollAnchor(Guid Chat, string Message, double Y, bool Body, double Offset, double Extent);
    private void InitializeTranscript()
    {
        _displayedChat = _model.SelectedChat?.Id;
        _model.PropertyChanged += TranscriptModelChanged;
        _manualScrollTimer.Tick += ManualScrollSettled;
        Transcript.Loaded += TranscriptLoaded;
        Transcript.PreviewMouseDown += TranscriptPointerDown;
        PreviewKeyDown += TranscriptNavigationShortcut;
        Transcript.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(TranscriptScrollBarChanged), true);
        Transcript.AddHandler(MarkdownMessageView.VisualizationFollowUpRequestedEvent, new EventHandler<VisualizationFollowUpEventArgs>(VisualizationFollowUpRequested));
    }
    private void DisposeTranscript()
    {
        _model.PropertyChanged -= TranscriptModelChanged;
        _manualScrollTimer.Tick -= ManualScrollSettled;
        Transcript.Loaded -= TranscriptLoaded;
        Transcript.PreviewMouseDown -= TranscriptPointerDown;
        PreviewKeyDown -= TranscriptNavigationShortcut;
        Transcript.RemoveHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(TranscriptScrollBarChanged));
        Transcript.RemoveHandler(MarkdownMessageView.VisualizationFollowUpRequestedEvent, new EventHandler<VisualizationFollowUpEventArgs>(VisualizationFollowUpRequested));
        CancelHistoryRestore();
    }
    private void TranscriptModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountViewModel.CanLoadEarlier) or nameof(AccountViewModel.HasMessages) or nameof(AccountViewModel.DisplayMessages)) UpdateTranscriptNavigation();
        if (e.PropertyName != nameof(AccountViewModel.SelectedChat) || _displayedChat == _model.SelectedChat?.Id) return;
        CancelHistoryRestore(); _displayedChat = _model.SelectedChat?.Id; _followTail = true;
        ScrollToLatest();
        UpdateTranscriptNavigation();
        _ = _model.Explorer.EnsureCapabilitiesAsync();
    }
    private void TranscriptLoaded(object sender, RoutedEventArgs e) => UpdateTranscriptNavigation();
    private void TranscriptNavigationShortcut(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.Alt || key is not (Key.Up or Key.Down or Key.Enter)) return;
        e.Handled = true;
        var button = key switch
        {
            Key.Up => PreviousUserMessageButton,
            Key.Down => NextUserMessageButton,
            _ => ScrollToBottomButton
        };
        if (button.IsEnabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }
    private void UpdateTranscriptNavigation(ScrollViewer? scroll = null)
    {
        scroll ??= TranscriptScroller();
        var atTop = scroll is null || scroll.VerticalOffset <= TranscriptTopTolerance;
        // Keep the header height steady while scrolling so hiding the button cannot move the viewport.
        LoadEarlierButton.Visibility = !_model.CanLoadEarlier ? Visibility.Collapsed : atTop ? Visibility.Visible : Visibility.Hidden;
        var awayFromBottom = scroll is not null && scroll.ScrollableHeight - scroll.VerticalOffset > TranscriptBottomTolerance;
        ScrollToBottomButton.IsEnabled = _model.HasMessages && awayFromBottom && !_followTail;
        var targets = UserMessageTargets();
        PreviousUserMessageButton.IsEnabled = !_loadingPreviousUserMessage && (targets.Previous is not null || _model.CanLoadEarlier);
        PreviousUserMessageButton.ToolTip = (_loadingPreviousUserMessage ? "Loading earlier messages…"
            : targets.Previous is null && _model.CanLoadEarlier ? "Load earlier history and go to the previous user message" : "Previous user message") + "\nAlt+Up";
        NextUserMessageButton.IsEnabled = !_loadingPreviousUserMessage && targets.Next is not null;
    }
    private (TranscriptEntry? Previous, TranscriptEntry? Next) UserMessageTargets()
    {
        var entries = _model.DisplayMessages;
        var current = _userMessageAnchor is { } key ? entries.FirstOrDefault(entry => entry.Role == "You" && entry.Key == key) : null;
        var y = 0d;
        if (current is null)
        {
            if (CaptureAnchor() is not { } viewport) return (null, null);
            current = entries.FirstOrDefault(entry => entry.Items.Any(message => message.Id == viewport.Message));
            y = viewport.Y;
        }
        if (current is null) return (null, null);
        var currentIndex = entries.IndexOf(current);
        TranscriptEntry? previous = null, next = null;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Role != "You") continue;
            if (index < currentIndex || index == currentIndex && y < -TranscriptTopTolerance) previous = entry;
            if (next is null && (index > currentIndex || index == currentIndex && y > TranscriptTopTolerance)) next = entry;
        }
        return (previous, next);
    }
    private async void PreviousUserMessage(object sender, RoutedEventArgs e) => await RunAsync(() => NavigateUserMessageAsync(false));
    private async void NextUserMessage(object sender, RoutedEventArgs e) => await RunAsync(() => NavigateUserMessageAsync(true));
    private async Task NavigateUserMessageAsync(bool next)
    {
        if (_loadingPreviousUserMessage) return;
        var targets = UserMessageTargets();
        var target = next ? targets.Next : targets.Previous;
        if (target is null && (next || !_model.CanLoadEarlier)
            || _model.SelectedChat is not { } chat || TranscriptScroller() is not { } scroll) return;
        var viewport = CaptureAnchor();
        var loadingEarlier = target is null;
        CancelHistoryRestore();
        _followTail = false; _preservingHistory = true;
        if (loadingEarlier) _loadingPreviousUserMessage = true;
        var version = _historyRestoreVersion;
        try
        {
            UpdateTranscriptNavigation(scroll);
            if (target is null)
            {
                if (_model.SelectedProject is not { } project) return;
                target = await LoadPreviousUserMessageAsync(project, chat, viewport, version);
                if (target is null || _closing || version != _historyRestoreVersion || _model.SelectedChat?.Id != chat.Id) return;
            }
            _userMessageAnchor = target.Key;
            var anchor = new ScrollAnchor(chat.Id, target.Message.Id, 0, false, scroll.VerticalOffset, scroll.ExtentHeight);
            UpdateTranscriptNavigation();
            await ScrollToAnchorSmoothlyAsync(anchor, version);
            if (_closing || version != _historyRestoreVersion) return;
            await Task.Delay(180); // Let virtualized message rendering settle before aligning its top.
            await ScrollToAnchorSmoothlyAsync(anchor, version);
            await Dispatcher.InvokeAsync(() => UpdateTranscriptNavigation(), DispatcherPriority.Background);
        }
        finally
        {
            if (loadingEarlier) _loadingPreviousUserMessage = false;
            if (version == _historyRestoreVersion) _preservingHistory = false;
            if (!_closing) UpdateTranscriptNavigation();
        }
    }
    private async Task<TranscriptEntry?> LoadPreviousUserMessageAsync(ProjectState project, ChatState chat, ScrollAnchor? viewport, int version)
    {
        var state = Runtime.Conversation(chat);
        var existingUsers = state.Messages.Where(message => message.Role == "You").Select(message => message.Id).ToHashSet();
        var cursors = new HashSet<(bool Turns, string Cursor)>();
        while (!_closing && version == _historyRestoreVersion && _model.SelectedChat?.Id == chat.Id && _model.CanLoadEarlier
            && !state.Loading && !state.Busy && !state.Uncertain && state.HistoryCursor is { } cursor)
        {
            if (!cursors.Add((state.UsesTurnHistory, cursor)))
                throw new IOException("Codex repeated a history cursor. Refresh the chat before going back again.");
            await Runtime.LoadChatAsync(project, chat, true);
            if (_closing || version != _historyRestoreVersion || _model.SelectedChat?.Id != chat.Id || state.Busy || state.Uncertain) return null;
            var previous = state.Transcript.LastOrDefault(entry => entry.Role == "You" && !existingUsers.Contains(entry.Key));
            if (previous is not null)
            {
                if (viewport is not null) { RestoreAnchor(viewport, version); Transcript.UpdateLayout(); }
                return previous;
            }
            if (viewport is not null)
            {
                await Dispatcher.InvokeAsync(() => RestoreAnchor(viewport, version), DispatcherPriority.Loaded);
                await Task.Delay(180);
                await Dispatcher.InvokeAsync(() => RestoreAnchor(viewport, version), DispatcherPriority.Loaded);
            }
        }
        return null;
    }
    private async void JumpToBottom(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (TranscriptScroller() is not { } scroll) return;
        CancelHistoryRestore();
        _followTail = false; _preservingHistory = true;
        var version = _historyRestoreVersion;
        try
        {
            UpdateTranscriptNavigation(scroll);
            await SmoothScrollToAsync(scroll, () => scroll.ScrollableHeight, version);
            if (!_closing && version == _historyRestoreVersion) _followTail = true;
        }
        finally
        {
            if (version == _historyRestoreVersion) _preservingHistory = false;
            if (!_closing) UpdateTranscriptNavigation();
        }
    });
    private async Task ScrollToAnchorSmoothlyAsync(ScrollAnchor anchor, int version)
    {
        if (_closing || version != _historyRestoreVersion || _model.SelectedChat?.Id != anchor.Chat || TranscriptScroller() is not { } scroll) return;
        var destination = TranscriptAnchorOffset(anchor, scroll);
        if (destination is null)
        {
            var viewport = CaptureAnchor();
            var offset = scroll.VerticalOffset;
            // Measure an unrealized row, then restore the viewport before the next frame.
            RestoreAnchor(anchor, version); Transcript.UpdateLayout();
            destination = TranscriptAnchorOffset(anchor, scroll);
            if (viewport is not null) RestoreAnchor(viewport, version);
            else scroll.ScrollToVerticalOffset(offset);
            Transcript.UpdateLayout();
        }
        if (destination is not { } target) return;
        await SmoothScrollToAsync(scroll, () => TranscriptAnchorOffset(anchor, scroll) ?? target, version);
    }
    private Task SmoothScrollToAsync(ScrollViewer scroll, Func<double> destination, int version)
    {
        if (_closing || version != _historyRestoreVersion || !IsLoaded || !IsVisible) return Task.CompletedTask;
        var start = scroll.VerticalOffset;
        var target = Math.Clamp(destination(), 0, scroll.ScrollableHeight);
        if (!SystemParameters.ClientAreaAnimation || Math.Abs(target - start) < 0.5)
        { scroll.ScrollToVerticalOffset(target); return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        var elapsed = Stopwatch.StartNew();
        void Finish(Exception? error = null)
        {
            timer.Stop(); timer.Tick -= Tick;
            if (error is null) completion.TrySetResult();
            else completion.TrySetException(error);
        }
        void Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_closing || version != _historyRestoreVersion || !IsLoaded || !IsVisible) { Finish(); return; }
                var progress = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / 260);
                var eased = 1 - Math.Pow(1 - progress, 3);
                var end = Math.Clamp(destination(), 0, scroll.ScrollableHeight);
                scroll.ScrollToVerticalOffset(start + (end - start) * eased);
                if (progress >= 1) Finish();
            }
            catch (Exception error) { Finish(error); }
        }
        timer.Tick += Tick; timer.Start();
        return completion.Task;
    }
    private static DependencyObject? GetTranscriptParent(DependencyObject value) => value switch
    {
        FrameworkContentElement content => content.Parent,
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(value),
        _ => LogicalTreeHelper.GetParent(value)
    };
    private ScrollViewer? TranscriptScroller()
    {
        ScrollViewer? Find(DependencyObject parent)
        {
            if (parent is ScrollViewer scroll && ReferenceEquals(scroll.TemplatedParent, Transcript)) return scroll;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (Find(VisualTreeHelper.GetChild(parent, index)) is { } found) return found;
            return null;
        }
        return Find(Transcript);
    }
    private void ScrollToLatest()
    {
        if (!_followTail || _manualScrolling || _preservingHistory || _closing || _tailScrollQueued) return;
        _tailScrollQueued = true;
        // Let layout settle and queued user input run before following the new extent.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _tailScrollQueued = false;
            if (!_followTail || _manualScrolling || _preservingHistory || _closing || !IsLoaded) return;
            TranscriptScroller()?.ScrollToEnd();
        }));
    }
    private void TranscriptMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || TranscriptScroller() is not { } outer) return;
        CancelHistoryRestore(); BeginManualScroll();
        for (var current = e.OriginalSource as DependencyObject; current is not null && !ReferenceEquals(current, outer); current = GetTranscriptParent(current))
        {
            if (current is not ScrollViewer inner || inner.ScrollableHeight <= 0 || inner.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) continue;
            if (e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight) return;
        }
        e.Handled = true;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == -1)
        {
            _followTail = e.Delta < 0 && outer.VerticalOffset + outer.ViewportHeight >= outer.ScrollableHeight;
            if (e.Delta > 0) outer.PageUp(); else outer.PageDown();
        }
        else
        {
            var target = outer.VerticalOffset - e.Delta / 120.0 * Math.Max(1, lines) * 18;
            _followTail = e.Delta < 0 && target >= outer.ScrollableHeight;
            outer.ScrollToVerticalOffset(target);
        }
    }
    private void TranscriptScrollBarChanged(object sender, ScrollEventArgs e)
    {
        if (e.OriginalSource is not ScrollBar { Orientation: Orientation.Vertical } bar || !ReferenceEquals(bar.TemplatedParent, TranscriptScroller())) return;
        CancelHistoryRestore();
        _scrollBarInteraction = e.ScrollEventType != ScrollEventType.EndScroll && Mouse.LeftButton == MouseButtonState.Pressed;
        BeginManualScroll();
        _followTail = e.NewValue >= bar.Maximum;
        UpdateTranscriptNavigation();
    }
    private void TranscriptScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scroll || !ReferenceEquals(scroll.TemplatedParent, Transcript)) return;
        if (_preservingHistory) { UpdateTranscriptNavigation(scroll); return; }
        var atBottom = scroll.ScrollableHeight - scroll.VerticalOffset <= TranscriptBottomTolerance;
        var layoutChanged = e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0;
        if (!layoutChanged && e.VerticalChange != 0) _userMessageAnchor = null;
        if (_manualScrolling) { UpdateTranscriptNavigation(scroll); return; }
        var followAnimation = atBottom && IsTranscriptAnimating;
        if (followAnimation) _followTail = true;
        else if (!layoutChanged && e.VerticalChange < 0) _followTail = false;
        else if (!layoutChanged && e.VerticalChange > 0 && atBottom) _followTail = true;
        if (_followTail && (followAnimation || layoutChanged || !atBottom && e.VerticalChange > 0)) ScrollToLatest();
        UpdateTranscriptNavigation(scroll);
    }
    private void EditMessage(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (!_model.CanEditMessages || sender is not FrameworkElement { DataContext: TranscriptEntry entry } || !entry.Message.CanEdit
            || _model.SelectedProject is not { } project || _model.SelectedChat is not { } chat || _model.SelectedModel is not { } model) return;
        var edit = Runtime.PrepareMessageEdit(entry.Message);
        var references = _model.MentionEntries.Concat(entry.Message.InputReferences).DistinctBy(reference => (reference.Kind, reference.Path)).ToArray();
        var effort = _model.Effort; var approvalMode = _model.ApprovalMode; var fastMode = _model.FastMode;
        var dialog = PrepareDialog(new EditMessageWindow(Runtime, edit, references,
            () => !_closing && _model.SelectedChat?.Id == chat.Id && _model.CanEditMessages,
            (text, attachments) => Runtime.SendAsync(project, chat, text, model, effort, approvalMode, references, attachments, fastMode, replacement: edit)));
        if (dialog.ShowDialog() == true && _model.SelectedChat?.Id == chat.Id)
        {
            CancelHistoryRestore(); _followTail = true; ScrollToLatest(); Composer.Focus();
        }
    });
    private void AnnotationCommentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        Composer.Focus();
        Composer.CaretIndex = Composer.Text.Length;
    }
    private void VisualizationFollowUpRequested(object? sender, VisualizationFollowUpEventArgs e)
    {
        e.Handled = true;
        if (_closing || !_model.CanCompose) return;
        Guard(() =>
        {
            Composer.CaretIndex = Composer.Text.Length;
            Composer.InsertText((string.IsNullOrWhiteSpace(Composer.Text) ? "" : Environment.NewLine + Environment.NewLine) + e.Prompt);
            Composer.Focus();
            Composer.CaretIndex = Composer.Text.Length;
        });
    }
    private void AnnotateSelection(object sender, AnnotationEventArgs e)
    {
        e.Handled = true;
        Guard(() =>
        {
            _model.AddAnnotation(e.Text, e.ImagePath);
            var quote = _model.DraftAnnotations.LastOrDefault();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (quote is null) return;
                DraftQuotes.UpdateLayout();
                var input = Descendants(DraftQuotes).OfType<TextBox>().FirstOrDefault(box => box.Name == "AnnotationComment" && ReferenceEquals(box.DataContext, quote));
                input?.BringIntoView(); input?.Focus();
            }));
        });
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private void BeginManualScroll()
    {
        _manualScrolling = true; _followTail = false;
        _manualScrollTimer.Stop(); _manualScrollTimer.Start();
    }
    private void ManualScrollSettled(object? sender, EventArgs e)
    {
        if (_scrollBarInteraction && Mouse.LeftButton == MouseButtonState.Pressed) return;
        _manualScrollTimer.Stop(); _manualScrolling = false; _scrollBarInteraction = false;
        if (_closing || !IsLoaded || _preservingHistory || TranscriptScroller() is not { } scroll) return;
        var remaining = scroll.ScrollableHeight - scroll.VerticalOffset;
        var animating = IsTranscriptAnimating;
        _followTail = _followTail || remaining <= 0 || remaining <= TranscriptBottomTolerance && animating;
        if (_followTail) ScrollToLatest();
        UpdateTranscriptNavigation(scroll);
    }
    private void CancelHistoryRestore()
    {
        _historyRestoreVersion++; _preservingHistory = false; _userMessageAnchor = null;
        _manualScrollTimer.Stop(); _manualScrolling = false; _scrollBarInteraction = false;
    }
    private void TranscriptPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (_preservingHistory) CancelHistoryRestore();
        for (var current = e.OriginalSource as DependencyObject; current is not null && !ReferenceEquals(current, Transcript); current = GetTranscriptParent(current))
            if (current is ScrollBar { Orientation: Orientation.Vertical } bar && ReferenceEquals(bar.TemplatedParent, TranscriptScroller()))
            {
                CancelHistoryRestore(); _scrollBarInteraction = true; BeginManualScroll();
                return;
            }
    }
    private ScrollAnchor? CaptureAnchor()
    {
        if (_model.SelectedChat is not { } chat || TranscriptScroller() is not { } scroll) return null;
        foreach (var entry in _model.DisplayMessages)
        {
            if (Transcript.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement { IsVisible: true } container) continue;
            var y = container.TranslatePoint(new Point(), Transcript).Y;
            if (y + container.ActualHeight <= 0 || y >= Transcript.ActualHeight) continue;
            if (entry.IsActivity && entry.Expanded)
                foreach (var view in Descendants(container).OfType<MarkdownMessageView>().Where(view => view.IsVisible))
                {
                    var bodyY = view.TranslatePoint(new Point(), Transcript).Y;
                    if (bodyY + view.ActualHeight > 0 && view.DataContext is ChatMessage message)
                        return new(chat.Id, message.Id, bodyY, true, scroll.VerticalOffset, scroll.ExtentHeight);
                }
            return new(chat.Id, entry.Message.Id, y, false, scroll.VerticalOffset, scroll.ExtentHeight);
        }
        return null;
    }
    private void RestoreAnchor(ScrollAnchor anchor, int version)
    {
        if (_closing || version != _historyRestoreVersion || _model.SelectedChat?.Id != anchor.Chat) return;
        var entry = _model.DisplayMessages.FirstOrDefault(candidate => candidate.Items.Any(message => message.Id == anchor.Message));
        if (entry is null) return; // The message may have been removed during a refresh.
        Transcript.ScrollIntoView(entry); Transcript.UpdateLayout();
        if (TranscriptScroller() is not { } scroll) return;
        scroll.ScrollToVerticalOffset(TranscriptAnchorOffset(anchor, scroll) ?? anchor.Offset + scroll.ExtentHeight - anchor.Extent);
    }
    private double? TranscriptAnchorOffset(ScrollAnchor anchor, ScrollViewer scroll)
    {
        var entry = _model.DisplayMessages.FirstOrDefault(candidate => candidate.Items.Any(message => message.Id == anchor.Message));
        if (entry is null || Transcript.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement { IsArrangeValid: true } container) return null;
        FrameworkElement target = anchor.Body ? Descendants(container).OfType<MarkdownMessageView>()
            .FirstOrDefault(view => view.IsVisible && view.DataContext is ChatMessage message && message.Id == anchor.Message) ?? container : container;
        var y = target.TranslatePoint(new Point(), Transcript).Y;
        return scroll.VerticalOffset + y - anchor.Y;
    }
    private async Task LoadEarlierKeepingPositionAsync()
    {
        if (!_model.CanLoadEarlier || _model.SelectedProject is not { } project || _model.SelectedChat is not { } chat) return;
        var anchor = CaptureAnchor(); var version = ++_historyRestoreVersion;
        _userMessageAnchor = null;
        _followTail = false; _preservingHistory = true;
        try
        {
            await Runtime.LoadChatAsync(project, chat, true);
            if (anchor is not null)
            {
                await Dispatcher.InvokeAsync(() => RestoreAnchor(anchor, version), DispatcherPriority.Loaded);
                await Task.Delay(180); // Allow coalesced Markdown layout to settle.
                await Dispatcher.InvokeAsync(() => RestoreAnchor(anchor, version), DispatcherPriority.Loaded);
            }
        }
        finally
        {
            if (version == _historyRestoreVersion) _preservingHistory = false;
            UpdateTranscriptNavigation();
        }
    }
    private void RemoveAnnotation(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.DataContext is ChatAnnotation quote) _model.RemoveAnnotation(quote); }
    private void InitializeAttachmentDrag(object sender, EventArgs e)
    {
        if (sender is Button button)
            FileLinks.EnableDrag(button, () => (button.DataContext as ChatAttachment)?.Path);
    }
    private void OpenAttachment(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.DataContext is ChatAttachment item) FileLinks.Open(item.Path); }
    private void RemoveAttachment(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.DataContext is ChatAttachment item) _model.RemoveAttachment(item); }
    private void AddAttachments(IEnumerable<string> paths)
    {
        foreach (var path in paths) _model.AddAttachment(AttachmentStore.FromFile(path));
    }
    private void ChooseAttachments(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var picker = new OpenFileDialog { Title = "Attach files or images", Multiselect = true, CheckFileExists = true, Filter = "All files|*.*" };
        if (picker.ShowDialog(HostWindow) == true) AddAttachments(picker.FileNames);
    });
    private static bool HasAttachmentData(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop)
        || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent("PNG");
    private static bool HasTextData(IDataObject data) => data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);
    private void ChatAttachmentDragOver(object sender, DragEventArgs e)
    {
        try
        {
            var attachment = HasAttachmentData(e.Data);
            var supported = attachment || HasTextData(e.Data);
            var canDrop = supported && !_closing && _model.CanCompose && (e.AllowedEffects & DragDropEffects.Copy) != 0;
            ChatDropHint.Text = attachment ? "Drop files or images to attach" : "Drop text into your draft";
            ChatDropOverlay.Visibility = canDrop ? Visibility.Visible : Visibility.Collapsed;
            if (!supported) return;
            e.Handled = true;
            e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        { e.Handled = true; e.Effects = DragDropEffects.None; ChatDropOverlay.Visibility = Visibility.Collapsed; }
    }
    private void ChatAttachmentDragLeave(object sender, DragEventArgs e) => ChatDropOverlay.Visibility = Visibility.Collapsed;
    private void ChatAttachmentDrop(object sender, DragEventArgs e)
    {
        ChatDropOverlay.Visibility = Visibility.Collapsed;
        Guard(() =>
        {
            var attachment = HasAttachmentData(e.Data);
            if (!attachment && !HasTextData(e.Data)) return;
            e.Handled = true; e.Effects = DragDropEffects.None;
            if (!_model.CanCompose || (e.AllowedEffects & DragDropEffects.Copy) == 0) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                AddAttachments(paths);
            else if (attachment)
            {
                if (_model.DraftAttachments.Count >= 20) throw new InvalidOperationException("Attach up to 20 files and images per message.");
                var image = DroppedImage(e.Data) ?? throw new IOException("The dropped image is unavailable. Save it as a file and try again.");
                _model.AddAttachment(AttachmentStore.SaveClipboardImage(image));
            }
            else
            {
                var text = e.Data.GetData(DataFormats.UnicodeText) as string ?? e.Data.GetData(DataFormats.Text) as string;
                if (string.IsNullOrEmpty(text)) return;
                var point = e.GetPosition(Composer);
                var position = point.X >= 0 && point.Y >= 0 && point.X < Composer.ActualWidth && point.Y < Composer.ActualHeight
                    ? Composer.GetPositionFromPoint(point, true) : null;
                if (position is not null) Composer.CaretPosition = position;
                else Composer.CaretIndex = Composer.Text.Length;
                Composer.InsertText(text);
            }
            e.Effects = DragDropEffects.Copy;
            Composer.Focus();
        });
    }
    private static BitmapSource? DroppedImage(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource image) return image;
        if (!data.GetDataPresent("PNG") || data.GetData("PNG") is not Stream png) return null;
        if (png.CanSeek)
        {
            if (png.Length > AttachmentStore.MaxImageBytes) throw new IOException("Images must be 25 MB or smaller.");
            png.Position = 0;
        }
        return BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }
    private void PasteAttachments(object sender, DataObjectPastingEventArgs e)
    {
        if (!_model.CanCompose) return;
        try
        {
            // FileDrop takes priority because Explorer also publishes thumbnails for copied images.
            if (e.DataObject.GetDataPresent(DataFormats.FileDrop) && e.DataObject.GetData(DataFormats.FileDrop) is string[] paths)
            { e.CancelCommand(); Guard(() => AddAttachments(paths)); }
            else if (e.DataObject.GetDataPresent(DataFormats.Bitmap) || Clipboard.ContainsImage())
            {
                e.CancelCommand();
                Guard(() =>
                {
                    if (_model.DraftAttachments.Count >= 20) throw new InvalidOperationException("Attach up to 20 files and images per message.");
                    var image = Clipboard.GetImage() ?? throw new IOException("The clipboard image is unavailable.");
                    _model.EnsureDraftChat(); _model.AddAttachment(AttachmentStore.SaveClipboardImage(image));
                });
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        { e.CancelCommand(); ShowMessage("The clipboard is busy. Try pasting again.", "Paste attachment"); }
    }
}
