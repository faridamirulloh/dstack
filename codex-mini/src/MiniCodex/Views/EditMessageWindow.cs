using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MiniCodex.Codex;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.Views;

public sealed class EditMessageWindow : TaskActionWindow
{
    private readonly MentionInput _input;
    private readonly StackPanel _editor = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly List<(ChatAnnotation Quote, TextBox Comment)> _comments = [];
    private readonly List<ChatAttachment> _attachments;
    private readonly Button _save;
    private readonly Button _cancel;
    private readonly Func<bool> _canSubmit;
    private readonly Func<string, IReadOnlyList<ChatAttachment>, Task> _submit;
    private readonly AccountRuntime _runtime;
    private readonly MessageEdit _edit;
    private bool _saving;

    public EditMessageWindow(AccountRuntime runtime, MessageEdit edit, IReadOnlyList<ComposerReference> references,
        Func<bool> canSubmit, Func<string, IReadOnlyList<ChatAttachment>, Task> submit) : base("Edit message", 650)
    {
        _runtime = runtime; _edit = edit; _canSubmit = canSubmit; _submit = submit;
        var parsed = MessagePayload.Parse(edit.OriginalText);
        _attachments = parsed.Attachments.ToList();
        Body.Children.Add(new TextBlock { Text = "Save replaces this message and removes the conversation after it, then starts a new reply. Existing file changes stay on disk.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        foreach (var quote in parsed.Annotations)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = quote.Preview, ToolTip = quote.Quote, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var comment = new TextBox { Text = quote.Comment, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 45, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 8000 };
            AutomationProperties.SetName(comment, "Annotation comment");
            card.Children.Add(comment); _comments.Add((quote, comment));
            _editor.Children.Add(new Border { Style = (Style)FindResource("ChatComposerSurface"), Margin = new Thickness(0, 0, 0, 8), Child = card });
        }
        foreach (var attachment in _attachments.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var remove = new Button { Content = "Remove", Style = (Style)FindResource("QuietButton"), FontSize = 10, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var name = new TextBlock { Text = attachment.Name, ToolTip = attachment.Path, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            FileLinks.EnableDrag(name, () => attachment.Path); row.Children.Add(name);
            remove.Click += (_, _) => { _attachments.Remove(attachment); _editor.Children.Remove(row); };
            _editor.Children.Add(row);
        }
        _input = new MentionInput { Style = (Style)FindResource("ChatComposerInput"), Text = parsed.Body, References = references, MinHeight = 150, MaxHeight = 300,
            ToolTip = "Enter saves and restarts the reply. Shift+Enter adds a new line." };
        AutomationProperties.SetName(_input, "Edited message");
        _input.PreviewKeyDown += async (_, e) =>
        {
            if (_input.HandleListKey(e.Key, Keyboard.Modifiers)) { e.Handled = true; return; }
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true; await SaveAsync();
        };
        _editor.Children.Add(new Border { Style = (Style)FindResource("ChatComposerSurface"), Child = _input });
        Body.Children.Add(_editor);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush"); Body.Children.Add(_error);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _cancel = ActionButton("Cancel", (_, _) => DialogResult = false); _cancel.IsCancel = true; actions.Children.Add(_cancel);
        _save = ActionButton("Save and restart", async (_, _) => await SaveAsync()); _save.Style = (Style)FindResource("AccentButton");
        actions.Children.Add(_save); Body.Children.Add(actions);
        runtime.Updated += Update;
        Closed += (_, _) => runtime.Updated -= Update;
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
        Loaded += (_, _) => { _input.Focus(); _input.CaretIndex = _input.Text.Length; };
        Update(this, EventArgs.Empty);
    }

    private void Update(object? sender, EventArgs e) => _save.IsEnabled = !_saving && _canSubmit();

    private async Task SaveAsync()
    {
        if (_saving || !_canSubmit()) return;
        if (string.IsNullOrWhiteSpace(_input.Text) && _comments.Count == 0 && _attachments.Count == 0) { _error.Text = "Enter a message."; _input.Focus(); return; }
        _saving = true; _editor.IsEnabled = false; _cancel.IsEnabled = false; Update(this, EventArgs.Empty);
        _error.Text = "Saving and restarting reply…";
        try
        {
            var annotations = _comments.Select(item => new ChatAnnotation(item.Quote.Quote, item.Comment.Text)).ToArray();
            MessagePayload.ValidateAnnotations(annotations); AttachmentStore.Validate(_attachments);
            await _submit(MessagePayload.Compose(_input.Text, annotations, _attachments), _attachments.ToArray());
            _saving = false; DialogResult = true;
        }
        catch (Exception error)
        {
            _runtime.Report(error);
            _error.Text = (_edit.HistoryReverted ? "History was updated. Your edit remains here. " : "") + error.Message;
            if (_edit.HistoryReverted) { _save.Content = "Send edited message"; _cancel.Content = "Close"; }
        }
        finally { _saving = false; _editor.IsEnabled = true; _cancel.IsEnabled = true; Update(this, EventArgs.Empty); }
    }
}
