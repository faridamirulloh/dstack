using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniCodex.Codex;
using MiniCodex.Models;
using MiniCodex.Presentation;

namespace MiniCodex.Views;

public abstract class TaskActionWindow : Window
{
    protected StackPanel Body { get; } = new() { Margin = new Thickness(22) };
    protected TaskActionWindow(string title, double width = 500)
    {
        Title = title; Width = width; MinWidth = width - 80; MaxHeight = 720;
        SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Body.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
    }
    protected static Button ActionButton(string label, RoutedEventHandler action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 10, 7, 0) }; button.Click += action; return button;
    }
}

public sealed class BusySendWindow : TaskActionWindow
{
    public string Choice { get; private set; } = "Queue";
    public BusySendWindow(bool canSteer) : base("Send while a task is running", 470)
    {
        Body.Children.Add(new TextBlock { Text = "Steer adds instructions to the current task using its current settings. Queue saves this message for after successful completion.", TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel();
        var steer = ActionButton("Steer now (S)", (_, _) => { Choice = "Steer"; DialogResult = true; }); steer.IsEnabled = canSteer;
        System.Windows.Automation.AutomationProperties.SetAcceleratorKey(steer, "S");
        buttons.Children.Add(steer);
        var queue = ActionButton("Queue next (Q)", (_, _) => { Choice = "Queue"; DialogResult = true; });
        System.Windows.Automation.AutomationProperties.SetAcceleratorKey(queue, "Q");
        buttons.Children.Add(queue);
        buttons.Children.Add(ActionButton("Cancel", (_, _) => DialogResult = false)); Body.Children.Add(buttons);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Handled || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
            var action = e.Key switch { Key.S => steer, Key.Q => queue, _ => null };
            if (action?.IsEnabled != true) return;
            e.Handled = true;
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
    }
}

public sealed class CodeReviewWindow : TaskActionWindow
{
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "Uncommitted changes", "Against base branch", "Specific commit", "Custom instructions" }, SelectedIndex = 0 };
    private readonly TextBlock _label = new() { Margin = new Thickness(0, 12, 0, 5), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _input = new() { MinHeight = 80, MaxHeight = 200, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 12000 };
    public object Target => _kind.SelectedIndex switch
    {
        0 => new { type = "uncommittedChanges" },
        1 => (object)new { type = "baseBranch", branch = _input.Text.Trim() },
        2 => new { type = "commit", sha = _input.Text.Trim() },
        _ => new { type = "custom", instructions = _input.Text.Trim() }
    };
    public CodeReviewWindow() : base("Start code review")
    {
        Body.Children.Add(new TextBlock { Text = "Review runs in this chat and uses its project folders. Review results appear in the conversation.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Body.Children.Add(_kind); Body.Children.Add(_label); Body.Children.Add(_input);
        _kind.SelectionChanged += (_, _) => UpdateFields(); UpdateFields();
        Body.Children.Add(ActionButton("Start review", (_, _) =>
        {
            if (_kind.SelectedIndex > 0 && string.IsNullOrWhiteSpace(_input.Text)) { _input.Focus(); return; }
            DialogResult = true;
        }));
    }
    private void UpdateFields()
    {
        _label.Text = _kind.SelectedIndex switch { 1 => "Base branch or ref", 2 => "Commit SHA", 3 => "Review instructions", _ => "Reviews staged, unstaged, and untracked changes." };
        _input.Visibility = _kind.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}

public sealed class EditQueuedWindow : TaskActionWindow
{
    private readonly MentionInput _input;
    private readonly QueuedMessage _message;
    private readonly List<(ChatAnnotation Quote, TextBox Comment)> _comments = [];
    public EditQueuedWindow(QueuedMessage message, IReadOnlyList<CodexModel> models) : base("Edit queued message", 610)
    {
        _message = message;
        var modelPicker = new ComboBox { ItemsSource = models, DisplayMemberPath = nameof(CodexModel.Name), SelectedItem = models.FirstOrDefault(model => model.Id == message.ModelId), Margin = new Thickness(0, 0, 0, 8) };
        var effortPicker = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        void SetEfforts()
        {
            if (modelPicker.SelectedItem is not CodexModel selected) return;
            effortPicker.ItemsSource = selected.Efforts;
            effortPicker.SelectedItem = selected.Efforts.FirstOrDefault(effort => effort.Equals(message.Effort, StringComparison.OrdinalIgnoreCase)) ?? selected.DefaultEffort;
        }
        modelPicker.SelectionChanged += (_, _) => SetEfforts(); SetEfforts();
        Body.Children.Add(modelPicker); Body.Children.Add(effortPicker);
        var parsed = MessagePayload.Parse(message.Text);
        _input = new MentionInput { Style = (Style)FindResource("ChatComposerInput"), Text = parsed.Body, References = message.References, MinHeight = 130, MaxHeight = 260 };
        System.Windows.Automation.AutomationProperties.SetName(_input, "Queued message draft");
        _input.PreviewKeyDown += (_, e) =>
        {
            var modifiers = Keyboard.Modifiers;
            if (e.Key == Key.Enter) modifiers |= ModifierKeys.Shift;
            if (_input.HandleListKey(e.Key, modifiers)) e.Handled = true;
        };
        var composer = new StackPanel();
        var hint = new TextBlock { Text = "Shift+Enter continues a list \u00b7 Shift+Tab indents", FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "FaintBrush");
        composer.Children.Add(hint);
        foreach (var quote in parsed.Annotations)
        {
            var annotation = new StackPanel();
            var preview = new TextBlock { Text = quote.Preview, ToolTip = quote.Quote, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            preview.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); annotation.Children.Add(preview);
            var comment = new TextBox { Text = quote.Comment, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 45, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            annotation.Children.Add(comment);
            composer.Children.Add(new Border { Style = (Style)FindResource("ChatComposerSurface"), Margin = new Thickness(0, 0, 0, 8), Child = annotation });
            _comments.Add((quote, comment));
        }
        var attachmentPanel = new FrameworkElementFactory(typeof(WrapPanel));
        composer.Children.Add(new ItemsControl { ItemsSource = message.Attachments, ItemTemplate = (DataTemplate)FindResource("QueuedAttachmentCard"), ItemsPanel = new ItemsPanelTemplate(attachmentPanel) });
        composer.Children.Add(_input);
        Body.Children.Add(new Border { Style = (Style)FindResource("ChatComposerSurface"), Child = composer });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = ActionButton("Cancel", (_, _) => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        var save = ActionButton("Save message", (_, _) =>
        {
            if (modelPicker.SelectedItem is not CodexModel selected) { modelPicker.Focus(); return; }
            if (string.IsNullOrWhiteSpace(_input.Text) && _comments.Count == 0 && message.Attachments.Length == 0) return;
            var quotes = _comments.Select(item => new ChatAnnotation(item.Quote.Quote, item.Comment.Text)).ToArray();
            try { MessagePayload.ValidateAnnotations(quotes); _message.ModelId = selected.Id; _message.ModelName = selected.Name; _message.Effort = effortPicker.SelectedItem as string ?? selected.DefaultEffort; _message.Text = MessagePayload.Compose(_input.Text, quotes, message.Attachments.Select(file => file.Open()).ToArray()); DialogResult = true; }
            catch (Exception error) { AppDialog.Show(this, error.Message, "Could not save message"); }
        });
        save.Style = (Style)FindResource("AccentButton"); actions.Children.Add(save); Body.Children.Add(actions);
    }
}

public sealed class QueueWindow : TaskActionWindow
{
    private readonly AccountRuntime _runtime;
    private readonly ConversationState _state;
    private readonly ListBox _items;
    private readonly Button _steer;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    public QueueWindow(AccountRuntime runtime, ConversationState state) : base("Message queue", 650)
    {
        _runtime = runtime; _state = state;
        Body.Children.Add(_status);
        _items = new ListBox { ItemsSource = state.Queue, ItemTemplate = (DataTemplate)FindResource("QueuedMessageCard"), ItemContainerStyle = (Style)FindResource("QueuedMessageItem"), MinHeight = 150, MaxHeight = 320 };
        ScrollViewer.SetVerticalScrollBarVisibility(_items, ScrollBarVisibility.Auto);
        System.Windows.Automation.AutomationProperties.SetName(_items, "Queued messages");
        Body.Children.Add(new Border { Style = (Style)FindResource("ChatComposerSurface"), Child = _items });
        var actions = new WrapPanel();
        _steer = ActionButton("Steer", async (_, _) => await Steer());
        _steer.ToolTip = "Send the selected queued message into the running task now, using that task's model and settings.";
        actions.Children.Add(_steer);
        _items.SelectionChanged += (_, _) => Update(this, EventArgs.Empty);
        actions.Children.Add(ActionButton("Edit", (_, _) => Act(() =>
        {
            if (Selected() is not { } item) return;
            if (state.QueueDispatching || state.QueueEditing) throw new InvalidOperationException("Wait for submission or editing to finish.");
            state.QueueEditing = true; runtime.PauseQueue(state, "Queue paused for editing. Resume when ready.");
            try { new EditQueuedWindow(item, runtime.Models) { Owner = this }.ShowDialog(); }
            finally { state.QueueEditing = false; }
        })));
        actions.Children.Add(ActionButton("Move up", (_, _) => Act(() => { if (Selected() is { } item) runtime.MoveQueued(state, item, -1); })));
        actions.Children.Add(ActionButton("Move down", (_, _) => Act(() => { if (Selected() is { } item) runtime.MoveQueued(state, item, 1); })));
        actions.Children.Add(ActionButton("Remove", (_, _) => Act(() => { if (Selected() is { } item) runtime.RemoveQueued(state, item); })));
        actions.Children.Add(ActionButton("Pause", (_, _) => runtime.PauseQueue(state)));
        actions.Children.Add(ActionButton("Resume", async (_, _) => await Resume()));
        actions.Children.Add(ActionButton("Confirm resend", async (_, _) =>
        {
            if (Selected() is not { MayHaveBeenSent: true } item || state.QueueDispatching || state.Uncertain) return;
            if (AppDialog.Show(this, "This message may already have reached Codex. Check the conversation first. Allow it to be sent again?", "Confirm queued resend", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            item.MayHaveBeenSent = false; await Resume();
        }));
        Body.Children.Add(actions); runtime.Updated += Update;
        Closed += (_, _) => runtime.Updated -= Update;
        Update(this, EventArgs.Empty);
    }
    private QueuedMessage? Selected() => _items.SelectedItem as QueuedMessage;
    private void Act(Action action) { try { _runtime.Touch(); action(); } catch (Exception error) { AppDialog.Show(this, error.Message, "Message queue"); } }
    private async Task Resume() { try { await _runtime.ResumeQueueAsync(_state); } catch (Exception error) { AppDialog.Show(this, error.Message, "Message queue"); } }
    private async Task Steer()
    {
        if (Selected() is not { } item) return;
        try { await _runtime.SteerQueuedAsync(_state, item); }
        catch (Exception error) { if (IsLoaded) AppDialog.Show(this, error.Message, "Could not steer queued message"); }
    }
    private void Update(object? sender, EventArgs e)
    {
        if (_items.Items.Count == 1 && _items.SelectedIndex != 0) _items.SelectedIndex = 0;
        _status.Text = $"{_state.Queue.Count} queued · " + (_state.QueuePaused ? "Paused" : _state.Busy ? "Waiting for successful completion" : "Ready") + "\n" + _state.QueueNotice;
        _steer.IsEnabled = Selected() is { } item && _runtime.CanSteerQueued(_state, item);
    }
}
