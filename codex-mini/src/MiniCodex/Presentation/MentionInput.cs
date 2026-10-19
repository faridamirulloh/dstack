using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MiniCodex.Models;

namespace MiniCodex.Presentation;

// Text and selection indices use the wire representation; the document displays mention chips.
public sealed class MentionInput : RichTextBox
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(MentionInput),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, TextChangedExternally));
    public static readonly DependencyProperty ReferencesProperty = DependencyProperty.Register(nameof(References), typeof(IReadOnlyList<ComposerReference>), typeof(MentionInput),
        new PropertyMetadata(null, ReferencesChanged));
    private static readonly Regex Marker = new(@"\[(?<label>[/$@](?:\\.|[^\]\\\r\n])+)\]\((?<target>[^)\r\n]+)\)", RegexOptions.NonBacktracking);
    private static readonly Regex ListPrefix = new(@"^(?<indent>[ \t]*)(?:(?<marker>[-+*]|[0-9]{1,9}[.)])[ \t]+)?", RegexOptions.NonBacktracking);
    private static readonly Regex ListShortcut = new(@"^[ \t]*(?<marker>[-+*]|[0-9]{1,9}[.)]?)$", RegexOptions.NonBacktracking);
    private sealed record Chip(string Token, ComposerReference Reference);
    private sealed record ListMarker(string Token);
    private sealed record Segment(TextPointer Start, TextPointer End, string Text, InlineUIContainer? Chip = null);
    private bool _updating;
    public bool IsUpdating => _updating;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IReadOnlyList<ComposerReference>? References { get => (IReadOnlyList<ComposerReference>?)GetValue(ReferencesProperty); set => SetValue(ReferencesProperty, value); }
    public int CaretIndex { get => IndexOf(CaretPosition); set => CaretPosition = PositionAt(value); }
    public int SelectionStart => IndexOf(Selection.Start);
    public int SelectionLength => Math.Max(0, IndexOf(Selection.End) - SelectionStart);

    public MentionInput()
    {
        Document = new FlowDocument(new Paragraph { Margin = new Thickness(0) }) { PagePadding = new Thickness(0), FontSize = 13 };
        Document.SetResourceReference(FlowDocument.FontFamilyProperty, SystemFonts.MessageFontFamilyKey);
        Document.SetResourceReference(FlowDocument.FontStyleProperty, SystemFonts.MessageFontStyleKey);
        Document.SetResourceReference(FlowDocument.FontWeightProperty, SystemFonts.MessageFontWeightKey);
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(SelectionBrushProperty, "SelectionBrush");
        SetResourceReference(CaretBrushProperty, "AccentBrush");
        IsDocumentEnabled = true;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        DataObject.AddPastingHandler(this, PasteText);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, CopySelection, CanCopy));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, CutSelection, CanCopy));
        ContextMenu = CreateEditMenu();
        ContextMenuOpening += (_, _) => ContextMenu = CreateEditMenu();
    }
    private ContextMenu CreateEditMenu()
    {
        var menu = new ContextMenu();
        if (SpellCheck.IsEnabled && GetSpellingError(CaretPosition) is { } error)
        {
            var suggestions = error.Suggestions.Take(5).ToArray();
            foreach (var suggestion in suggestions)
                menu.Items.Add(new MenuItem
                {
                    Header = suggestion.Replace("_", "__"), FontWeight = FontWeights.SemiBold,
                    Command = EditingCommands.CorrectSpellingError, CommandParameter = suggestion, CommandTarget = this
                });
            if (suggestions.Length == 0) menu.Items.Add(new MenuItem { Header = "No spelling suggestions", IsEnabled = false });
            menu.Items.Add(new MenuItem { Header = "Ignore all", Command = EditingCommands.IgnoreSpellingError, CommandTarget = this });
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(new MenuItem { Header = "Cut", Command = ApplicationCommands.Cut, CommandTarget = this });
        menu.Items.Add(new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy, CommandTarget = this });
        menu.Items.Add(new MenuItem { Header = "Paste", Command = ApplicationCommands.Paste, CommandTarget = this });
        return menu;
    }
    private static void TextChangedExternally(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var input = (MentionInput)sender;
        if (!input._updating && input.Serialize() != (string?)e.NewValue)
        {
            input.Render(((string?)e.NewValue ?? "").ReplaceLineEndings("\n"), input.CaretIndex, clearUndo: true);
            if (input.Text != input.Serialize()) input.Publish();
        }
    }
    private static void ReferencesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var input = (MentionInput)sender;
        foreach (var segment in input.Segments())
            if (segment.Chip is { Tag: Chip chip } container)
            {
                var reference = input.References?.FirstOrDefault(entry => entry.Path.Equals(chip.Reference.Path, StringComparison.OrdinalIgnoreCase));
                if (reference is null || reference == chip.Reference) continue;
                container.Tag = chip with { Reference = reference }; container.Child = input.ChipButton(reference, container);
            }
    }
    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        if (!_updating) Publish();
        base.OnTextChanged(e);
    }
    private void Publish()
    {
        _updating = true;
        try { SetCurrentValue(TextProperty, Serialize()); }
        finally { _updating = false; }
    }
    public void Select(int start, int length) => Selection.Select(PositionAt(start), PositionAt(start + Math.Max(0, length)));

    public bool HandleListKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt)) return false;
        var text = Text;
        var start = Math.Clamp(SelectionStart, 0, text.Length);
        var end = Math.Clamp(start + SelectionLength, start, text.Length);
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var prefix = ListPrefix.Match(text[lineStart..]);
        var marker = prefix.Groups["marker"];
        if (key == Key.Space && start == end && ListShortcut.Match(text[lineStart..start]) is { Success: true } shortcut)
        {
            ReplaceText(start, 0, char.IsAsciiDigit(shortcut.Groups["marker"].Value[^1]) ? ". " : " ");
            return true;
        }
        if (key == Key.Enter && modifiers.HasFlag(ModifierKeys.Shift))
        {
            var continuation = "";
            if (marker.Success)
            {
                var lineEnd = text.IndexOf('\n', end);
                if (lineEnd < 0) lineEnd = text.Length;
                if (string.IsNullOrWhiteSpace(text[(lineStart + prefix.Length)..lineEnd]))
                {
                    ReplaceText(lineStart, lineEnd - lineStart, "");
                    return true;
                }
                continuation = prefix.Groups["indent"].Value + (char.IsDigit(marker.Value[0])
                    ? (long.Parse(marker.Value[..^1], System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + marker.Value[^1] + " "
                    : marker.Value + " ");
            }
            ReplaceText(start, end - start, "\n" + continuation);
            return true;
        }
        // Shift+Tab is the requested shortcut for a nested item; Tab works as well.
        if (key == Key.Tab && marker.Success)
        {
            var last = end > start && text[end - 1] == '\n' ? end - 1 : end;
            var blockEnd = text.IndexOf('\n', last);
            if (blockEnd < 0) blockEnd = text.Length;
            var lines = text[lineStart..blockEnd].Split('\n');
            if (lines.Any(line => !ListPrefix.Match(line).Groups["marker"].Success)) return false;
            var replacement = string.Join("\n", lines.Select(line => "    " + line));
            var selectedEnd = end + lines.Length * 4;
            Render(text[..lineStart] + replacement + text[blockEnd..], selectedEnd, clearUndo: false);
            if (end > start) Select(start + 4, selectedEnd - start - 4);
            Publish();
            return true;
        }
        if (key == Key.Back && start == end && marker.Success && start == lineStart + prefix.Length)
        {
            var indent = prefix.Groups["indent"].Value;
            ReplaceText(lineStart, indent.Length > 0 ? Math.Min(4, indent.Length) : prefix.Length, "", start - (indent.Length > 0 ? Math.Min(4, indent.Length) : prefix.Length));
            return true;
        }
        return false;
    }

    public void InsertText(string text) => ReplaceText(SelectionStart, SelectionLength, text.ReplaceLineEndings("\n"));

    private void ReplaceText(int start, int length, string replacement, int? caret = null)
    {
        Render(Text[..start] + replacement + Text[(start + length)..], caret ?? start + replacement.Length, clearUndo: false);
        Publish();
    }

    public void ToggleList(bool ordered)
    {
        var text = Text;
        var start = Math.Clamp(SelectionStart, 0, text.Length);
        var length = Math.Clamp(SelectionLength, 0, text.Length - start);
        var blockStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var last = start + length;
        if (length > 0 && text[last - 1] == '\n') last--;
        var blockEnd = text.IndexOf('\n', last);
        if (blockEnd < 0) blockEnd = text.Length;
        var lines = text[blockStart..blockEnd].Split('\n');
        var prefixes = lines.Select(line => ListPrefix.Match(line)).ToArray();
        var remove = lines.Select((line, index) => (line, index)).Where(item => lines.Length == 1 || !string.IsNullOrWhiteSpace(item.line))
            .All(item => prefixes[item.index].Groups["marker"] is { Success: true } marker && char.IsDigit(marker.Value[0]) == ordered);
        var formatted = new string[lines.Length];
        var newPrefixes = new int[lines.Length];
        var number = 1;
        for (var index = 0; index < lines.Length; index++)
        {
            var indent = prefixes[index].Groups["indent"].Value;
            if (lines.Length > 1 && string.IsNullOrWhiteSpace(lines[index])) { formatted[index] = lines[index]; newPrefixes[index] = prefixes[index].Length; continue; }
            var marker = remove ? "" : ordered ? (number++).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " : "- ";
            formatted[index] = indent + marker + lines[index][prefixes[index].Length..];
            newPrefixes[index] = indent.Length + marker.Length;
        }
        var replacement = string.Join("\n", formatted);
        var result = text[..blockStart] + replacement + text[blockEnd..];
        int MapPosition(int position)
        {
            if (position < blockStart) return position;
            if (position > blockEnd) return position + replacement.Length - (blockEnd - blockStart);
            var before = blockStart; var after = blockStart;
            for (var index = 0; index < lines.Length; index++)
            {
                if (position <= before + lines[index].Length)
                {
                    var column = position - before;
                    return after + (column < prefixes[index].Groups["indent"].Length ? column : newPrefixes[index] + Math.Max(0, column - prefixes[index].Length));
                }
                before += lines[index].Length + 1; after += formatted[index].Length + 1;
            }
            return blockStart + replacement.Length;
        }
        var mappedStart = MapPosition(start); var mappedEnd = MapPosition(start + length);
        Render(result, mappedEnd, clearUndo: false);
        if (length > 0) Select(mappedStart, mappedEnd - mappedStart);
        Publish();
    }

    public void InsertReference(ComposerReference reference, int start, int length)
    {
        var text = Text;
        var token = reference.Token + " ";
        var references = (References ?? []).Append(reference).DistinctBy(entry => entry.Path).ToArray();
        var result = text[..start] + token + text[(start + length)..];
        Render(result, start + token.Length, clearUndo: false, references);
        Publish();
    }
    private void Render(string text, int caret, bool clearUndo, IReadOnlyList<ComposerReference>? references = null)
    {
        _updating = true;
        var undo = IsUndoEnabled;
        if (clearUndo) IsUndoEnabled = false;
        BeginChange();
        try
        {
            Document.Blocks.Clear();
            foreach (var line in text.Split('\n'))
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) }; Document.Blocks.Add(paragraph);
                var prefix = ListPrefix.Match(line);
                var position = 0;
                if (prefix.Groups["marker"] is { Success: true } marker)
                {
                    // Keep the Markdown prefix in the wire text and display a hanging list marker.
                    var width = Math.Max(20, marker.Value.Length * 8 + 7);
                    var indent = prefix.Groups["indent"].Value.Sum(character => character == '\t' ? 4 : 1) * 7;
                    paragraph.Margin = new Thickness(indent + width, 0, 0, 0); paragraph.TextIndent = -width;
                    var label = new TextBlock { Text = char.IsDigit(marker.Value[0]) ? marker.Value : "\u2022", Width = width, Focusable = false, IsHitTestVisible = false };
                    label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    paragraph.Inlines.Add(new InlineUIContainer(label) { Tag = new ListMarker(prefix.Value), BaselineAlignment = BaselineAlignment.Baseline });
                    position = prefix.Length;
                }
                foreach (Match match in Marker.Matches(line))
                {
                    if (!TryReference(match, references ?? References ?? [], out var reference)) continue;
                    AppendText(paragraph, line[position..match.Index]);
                    var container = new InlineUIContainer { Tag = new Chip(match.Value, reference!), BaselineAlignment = BaselineAlignment.Center };
                    container.Child = ChipButton(reference!, container); paragraph.Inlines.Add(container);
                    position = match.Index + match.Length;
                }
                AppendText(paragraph, line[position..]);
            }
            CaretPosition = PositionAt(Math.Clamp(caret, 0, text.Length));
        }
        finally { EndChange(); IsUndoEnabled = undo; _updating = false; }
    }
    private static void AppendText(Paragraph paragraph, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        { if (index > 0) paragraph.Inlines.Add(new LineBreak()); if (lines[index].Length > 0) paragraph.Inlines.Add(new Run(lines[index])); }
    }
    private Button ChipButton(ComposerReference reference, InlineUIContainer container)
    {
        var color = reference.Kind switch
        {
            ReferenceKind.Plugin => "PluginMentionBrush", ReferenceKind.Skill => "SkillMentionBrush",
            ReferenceKind.Folder => "FolderMentionBrush", ReferenceKind.Connector => "ConnectorMentionBrush", _ => "FileLinkBrush"
        };
        var button = new Button
        {
            Content = reference.Name.Split(':').Last(), Focusable = false, IsTabStop = false,
            Padding = new Thickness(5, 1, 5, 1), MinHeight = 21, FontSize = 12, Margin = new Thickness(1, 0, 1, 0),
            Style = (Style)FindResource("QuietButton")
        };
        button.SetResourceReference(ForegroundProperty, color); button.SetResourceReference(BackgroundProperty, "RaisedBrush");
        button.ToolTip = new TextBlock
        {
            Text = reference.Kind is ReferenceKind.File or ReferenceKind.Folder ? reference.Path + "\nCtrl+click to open. Drag to copy the original file or folder."
                : reference.Description.Length > 0 ? reference.Description : reference.Name,
            MaxWidth = 450, TextWrapping = TextWrapping.Wrap
        };
        if (reference.Kind is ReferenceKind.File or ReferenceKind.Folder) FileLinks.EnableDrag(button, () => reference.Path);
        button.Click += (_, _) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (reference.Kind is ReferenceKind.File or ReferenceKind.Folder)) FileLinks.Open(reference.Path);
            else { Focus(); CaretPosition = container.ElementEnd.GetInsertionPosition(LogicalDirection.Forward) ?? container.ElementEnd; }
        };
        return button;
    }
    private static bool TryReference(Match match, IReadOnlyList<ComposerReference> references, out ComposerReference? reference)
    {
        var target = match.Groups["target"].Value;
        try { target = Uri.UnescapeDataString(target); } catch (UriFormatException) { }
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsFile) target = uri.LocalPath;
        reference = references.FirstOrDefault(entry => entry.Path.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (reference is not null) return true;
        var label = match.Groups["label"].Value;
        var kind = target.StartsWith("plugin://", StringComparison.Ordinal) ? ReferenceKind.Plugin
            : target.StartsWith("app://", StringComparison.Ordinal) ? ReferenceKind.Connector
            : (label.StartsWith('/') || label.StartsWith('$')) ? ReferenceKind.Skill : Directory.Exists(target) ? ReferenceKind.Folder : ReferenceKind.File;
        if ((kind is ReferenceKind.File or ReferenceKind.Folder or ReferenceKind.Skill) && !Path.IsPathFullyQualified(target)) return false;
        reference = new(kind, label[1..].Replace(@"\[", "[").Replace(@"\]", "]").Replace(@"\\", @"\"), target);
        return true;
    }
    private List<Segment> Segments()
    {
        var segments = new List<Segment>();
        Paragraph? previous = null;
        foreach (var paragraph in Document.Blocks.OfType<Paragraph>())
        {
            if (previous is not null) segments.Add(new(previous.ContentEnd, paragraph.ContentStart, "\n"));
            Add(paragraph.Inlines); previous = paragraph;
        }
        return segments;
        void Add(InlineCollection inlines)
        {
            foreach (var inline in inlines)
                switch (inline)
                {
                    case Run run: segments.Add(new(run.ContentStart, run.ContentEnd, run.Text)); break;
                    case LineBreak line: segments.Add(new(line.ElementStart, line.ElementEnd, "\n")); break;
                    case InlineUIContainer { Tag: Chip chip } container: segments.Add(new(container.ElementStart, container.ElementEnd, chip.Token, container)); break;
                    case InlineUIContainer { Tag: ListMarker marker } container: segments.Add(new(container.ElementStart, container.ElementEnd, marker.Token, container)); break;
                    case Span span: Add(span.Inlines); break;
                }
        }
    }
    private string Serialize()
    {
        var text = new StringBuilder(); foreach (var segment in Segments()) text.Append(segment.Text); return text.ToString();
    }
    private int IndexOf(TextPointer pointer)
    {
        var index = 0;
        foreach (var segment in Segments())
        {
            if (pointer.CompareTo(segment.Start) <= 0) return index;
            if (pointer.CompareTo(segment.End) < 0)
                return index + (segment.Chip is not null ? segment.Text.Length : Math.Clamp(segment.Start.GetOffsetToPosition(pointer), 0, segment.Text.Length));
            index += segment.Text.Length;
        }
        return index;
    }
    private TextPointer PositionAt(int index)
    {
        index = Math.Max(0, index);
        foreach (var segment in Segments())
        {
            if (index == 0) return segment.Start.GetInsertionPosition(LogicalDirection.Forward) ?? segment.Start;
            if (index <= segment.Text.Length)
            {
                if (segment.Chip is not null || segment.Text == "\n") return segment.End.GetInsertionPosition(LogicalDirection.Forward) ?? segment.End;
                return segment.Start.GetPositionAtOffset(index, LogicalDirection.Forward) ?? segment.End;
            }
            index -= segment.Text.Length;
        }
        return Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? Document.ContentEnd;
    }
    private void PasteText(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (e.DataObject.GetDataPresent(DataFormats.FileDrop) || e.DataObject.GetDataPresent(DataFormats.Bitmap) || e.DataObject.GetDataPresent("PNG")) return;
            if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text) return;
            e.CancelCommand();
            var start = SelectionStart; var length = SelectionLength;
            text = text.ReplaceLineEndings("\n");
            var result = Text[..start] + text + Text[(start + length)..];
            Render(result, start + text.Length, clearUndo: false); Publish();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        { e.CancelCommand(); AppDialog.Show("The clipboard is busy. Try pasting again.", "Paste"); }
    }
    private void CanCopy(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = SelectionLength > 0; e.Handled = true; }
    private void CopySelection(object sender, ExecutedRoutedEventArgs e)
    {
        try { if (SelectionLength > 0) Clipboard.SetText(Text.Substring(SelectionStart, SelectionLength)); }
        catch (System.Runtime.InteropServices.ExternalException) { AppDialog.Show("The clipboard is busy. Try copying again.", "Copy"); }
        e.Handled = true;
    }
    private void CutSelection(object sender, ExecutedRoutedEventArgs e)
    {
        try { if (SelectionLength > 0) { Clipboard.SetText(Text.Substring(SelectionStart, SelectionLength)); Selection.Text = ""; } }
        catch (System.Runtime.InteropServices.ExternalException) { AppDialog.Show("The clipboard is busy. Try cutting again.", "Cut"); }
        e.Handled = true;
    }
}
