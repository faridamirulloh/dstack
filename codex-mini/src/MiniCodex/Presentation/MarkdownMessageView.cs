using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Windows.Input;
using System.Globalization;
using System.Text;
using MiniCodex.Models;

namespace MiniCodex.Presentation;

public sealed class MarkdownMessageView : RichTextBox
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarkdownMessageView), new PropertyMetadata("", ContentChanged));
    public static readonly DependencyProperty RoleProperty = DependencyProperty.Register(nameof(Role), typeof(string), typeof(MarkdownMessageView), new PropertyMetadata("", ContentChanged));
    public static readonly DependencyProperty BaseDirectoryProperty = DependencyProperty.Register(nameof(BaseDirectory), typeof(string), typeof(MarkdownMessageView), new PropertyMetadata("", ContentChanged));
    public static readonly DependencyProperty MentionsProperty = DependencyProperty.Register(nameof(Mentions), typeof(IReadOnlyList<ComposerReference>), typeof(MarkdownMessageView), new PropertyMetadata(null, ContentChanged));
    public static readonly DependencyProperty IsStreamingProperty = DependencyProperty.Register(nameof(IsStreaming), typeof(bool), typeof(MarkdownMessageView), new PropertyMetadata(false, ContentChanged));
    public static readonly DependencyProperty FindTextProperty = DependencyProperty.Register(nameof(FindText), typeof(string), typeof(MarkdownMessageView), new PropertyMetadata("", ContentChanged));
    public static readonly RoutedEvent AnnotationRequestedEvent = EventManager.RegisterRoutedEvent(nameof(AnnotationRequested), RoutingStrategy.Bubble, typeof(EventHandler<AnnotationEventArgs>), typeof(MarkdownMessageView));
    public event EventHandler<AnnotationEventArgs> AnnotationRequested { add => AddHandler(AnnotationRequestedEvent, value); remove => RemoveHandler(AnnotationRequestedEvent, value); }
    public static readonly RoutedEvent VisualizationFollowUpRequestedEvent = EventManager.RegisterRoutedEvent(nameof(VisualizationFollowUpRequested), RoutingStrategy.Bubble, typeof(EventHandler<VisualizationFollowUpEventArgs>), typeof(MarkdownMessageView));
    public event EventHandler<VisualizationFollowUpEventArgs> VisualizationFollowUpRequested { add => AddHandler(VisualizationFollowUpRequestedEvent, value); remove => RemoveHandler(VisualizationFollowUpRequestedEvent, value); }
    public IReadOnlyList<ComposerReference>? Mentions { get => (IReadOnlyList<ComposerReference>?)GetValue(MentionsProperty); set => SetValue(MentionsProperty, value); }
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _reveal = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private string _shownText = "";
    private int _stableLength;
    private Run? _tail;
    private bool _revealing;
    private bool _renderQueued;
    private string? _renderedText;
    private string? _renderedRole;
    private string? _renderedBaseDirectory;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Role { get => (string)GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    public string BaseDirectory { get => (string)GetValue(BaseDirectoryProperty); set => SetValue(BaseDirectoryProperty, value); }
    public bool IsStreaming { get => (bool)GetValue(IsStreamingProperty); set => SetValue(IsStreamingProperty, value); }
    public bool IsRevealing => IsLoaded && IsVisible && _reveal.IsEnabled;
    public string FindText { get => (string)GetValue(FindTextProperty); set => SetValue(FindTextProperty, value); }

    public MarkdownMessageView()
    {
        IsReadOnly = true; IsDocumentEnabled = true; IsUndoEnabled = false;
        IsInactiveSelectionHighlightEnabled = true;
        BorderThickness = new Thickness(0); Padding = new Thickness(0);
        Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(SelectionBrushProperty, "SelectionBrush");
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var copy = new MenuItem { Header = "Copy selection" };
        copy.Click += (_, _) =>
        {
            try { if (!Selection.IsEmpty) Clipboard.SetText(Selection.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { AppDialog.Show("The clipboard is busy. Try copying again.", "Copy selection"); }
        };
        var annotate = new MenuItem { Header = "Annotate selection" };
        annotate.Click += (_, _) => RequestAnnotation(Selection.Text);
        ContextMenu = new ContextMenu { Items = { copy, annotate } };
        ContextMenu.Opened += (_, _) => { copy.IsEnabled = !Selection.IsEmpty; annotate.IsEnabled = !Selection.IsEmpty && Role is not ("Command" or "Files" or "Status"); };
        SelectionAnnotation.Attach(this, () => Selection.Text, () => CaretPosition.GetCharacterRect(LogicalDirection.Forward),
            () => Role is not ("Command" or "Files" or "Status"));
        _refresh.Tick += (_, _) => { _refresh.Stop(); Render(); };
        _reveal.Tick += (_, _) => RevealCharacters();
        DataContextChanged += (_, _) => ResetContent();
        Loaded += (_, _) => Render();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) Render(); else StopAnimation(); };
        Unloaded += (_, _) => StopAnimation();
    }

    private static void ContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownMessageView)sender;
        if (e.Property == BaseDirectoryProperty || e.Property == RoleProperty) { view.ResetContent(); return; }
        if (e.Property == FindTextProperty || e.Property == MentionsProperty)
        {
            view._renderedText = null;
            if (view.IsLoaded && view.IsVisible && !view._renderQueued) view.Render();
            return;
        }
        if (!view.IsLoaded || !view.IsVisible) return;
        if (view._renderQueued) return;
        if (view.Role == "Codex" && SystemParameters.ClientAreaAnimation && (view.IsStreaming || view._revealing)
            && (view.Text ?? "").StartsWith(view._shownText, StringComparison.Ordinal))
        {
            view._refresh.Stop(); view._revealing = true;
            if ((view.Text?.Length ?? 0) > view._shownText.Length) view._reveal.Start();
            else if (!view.IsStreaming) view.Render();
            return;
        }
        view._reveal.Stop(); view._revealing = false;
        if (!view._refresh.IsEnabled) view._refresh.Start();
    }

    private void StopAnimation()
    {
        _refresh.Stop(); _reveal.Stop(); _revealing = false;
        if (_tail is not null) _renderedText = null;
        _tail = null;
    }

    private void ResetContent()
    {
        StopAnimation(); _shownText = ""; _stableLength = 0; _renderedBaseDirectory = null;
        if (!IsLoaded || !IsVisible || _renderQueued) return;
        _renderQueued = true;
        // Retain the current document until the recycled row's bindings are ready.
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            _renderQueued = false;
            if (IsLoaded && IsVisible) { _refresh.Stop(); Render(); }
        }));
    }

    private void Render()
    {
        _reveal.Stop(); _revealing = false;
        if (!IsVisible || _tail is null && _renderedText == Text && _renderedRole == Role && _renderedBaseDirectory == BaseDirectory) return;
        Document = MarkdownDocument.Create(Text ?? "", Role ?? "", BaseDirectory ?? "", Mentions ?? []);
        _shownText = Text ?? ""; _tail = null;
        _renderedText = Text; _renderedRole = Role; _renderedBaseDirectory = BaseDirectory;
        HighlightFind();
    }
    private void RevealCharacters()
    {
        if (!IsLoaded || !IsVisible) { StopAnimation(); return; }
        var target = Text ?? "";
        if (Role != "Codex" || !target.StartsWith(_shownText, StringComparison.Ordinal)) { Render(); return; }
        var next = _shownText.Length;
        // Drain larger server chunks quickly without delaying the task or rebuilding Markdown per character.
        var count = Math.Max(1, (int)Math.Ceiling((target.Length - next) / 8.0));
        for (var index = 0; index < count && next < target.Length; index++)
            next += StringInfo.GetNextTextElementLength(target.AsSpan(next));
        if (next > _shownText.Length)
        {
            var shown = target[..next];
            var boundary = _tail is null || target.AsSpan(_shownText.Length, next - _shownText.Length).Contains('\n')
                ? StreamingBoundary(shown) : _stableLength;
            if (_tail is null || boundary != _stableLength)
            {
                Document = MarkdownDocument.Create(shown[..boundary], Role, BaseDirectory, Mentions ?? []);
                _stableLength = boundary; _tail = new Run();
                Document.Blocks.Add(new Paragraph(_tail) { Margin = new Thickness(0, 0, 0, 7) });
            }
            _tail.Text = shown[_stableLength..]; _shownText = shown; _renderedText = null;
        }
        if (next >= target.Length)
        {
            _reveal.Stop();
            if (!IsStreaming) Render();
        }
    }
    private static int StreamingBoundary(string text)
    {
        var boundary = 0; var position = 0; var fenceLength = 0; var fence = '\0';
        var previousLine = ""; var inTable = false;
        while (text.IndexOf('\n', position) is var end && end >= 0)
        {
            var line = text.AsSpan(position, end - position).TrimEnd();
            var trimmed = line.TrimStart();
            if (line.Length - trimmed.Length <= 3 && trimmed.Length >= 3 && trimmed[0] is '`' or '~')
            {
                var length = 1;
                while (length < trimmed.Length && trimmed[length] == trimmed[0]) length++;
                if (length >= 3)
                {
                    if (fenceLength == 0) { fence = trimmed[0]; fenceLength = length; }
                    else if (trimmed[0] == fence && length >= fenceLength && trimmed[length..].Trim().IsEmpty) fenceLength = 0;
                }
                previousLine = ""; inTable = false;
            }
            else if (fenceLength == 0)
            {
                var completedLine = line.ToString();
                if (trimmed.IsEmpty) { boundary = end + 1; inTable = false; }
                else if (inTable && trimmed.Contains('|')) boundary = end + 1;
                else
                {
                    inTable = MarkdownDocument.IsTableHeader(previousLine, completedLine);
                    if (inTable) boundary = end + 1;
                }
                previousLine = completedLine;
            }
            position = end + 1;
        }
        return boundary;
    }
    private sealed record FindSegment(int Start, int Length, TextPointer? Pointer, IMarkdownSearchContent? Embedded = null);
    private static IMarkdownSearchContent? EmbeddedContent(DependencyObject element)
    {
        if (element is IMarkdownSearchContent content) return content;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (EmbeddedContent(VisualTreeHelper.GetChild(element, index)) is { } child) return child;
        return null;
    }
    private (string Text, List<FindSegment> Segments) SearchDocument()
    {
        var text = new StringBuilder(); var segments = new List<FindSegment>();
        for (TextPointer? pointer = Document.ContentStart; pointer is not null && pointer.CompareTo(Document.ContentEnd) < 0;)
        {
            var context = pointer.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.Text)
            {
                var run = pointer.GetTextInRun(LogicalDirection.Forward);
                segments.Add(new(text.Length, run.Length, pointer)); text.Append(run);
                pointer = pointer.GetPositionAtOffset(run.Length, LogicalDirection.Forward);
            }
            else
            {
                if (context == TextPointerContext.EmbeddedElement && pointer.GetAdjacentElement(LogicalDirection.Forward) is DependencyObject embedded && EmbeddedContent(embedded) is { } content)
                {
                    text.Append('\n'); segments.Add(new(text.Length, content.SourceText.Length, null, content)); text.Append(content.SourceText).Append('\n');
                }
                if (context == TextPointerContext.ElementEnd && pointer.GetAdjacentElement(LogicalDirection.Forward) is Paragraph
                    || context == TextPointerContext.ElementStart && pointer.GetAdjacentElement(LogicalDirection.Forward) is LineBreak) text.Append('\n');
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
        }
        return (text.ToString(), segments);
    }
    private static TextPointer? FindPointer(List<FindSegment> segments, int offset, bool end)
    {
        foreach (var segment in segments)
            if (segment.Pointer is { } pointer && offset >= segment.Start && (end ? offset <= segment.Start + segment.Length : offset < segment.Start + segment.Length))
                return pointer.GetPositionAtOffset(offset - segment.Start, LogicalDirection.Forward);
        return null;
    }
    private void HighlightFind()
    {
        if (string.IsNullOrEmpty(FindText) || FindText.Length > 500) return;
        var (text, segments) = SearchDocument(); var ranges = new List<TextRange>(); var offset = 0;
        while (offset <= text.Length - FindText.Length && ranges.Count < 500)
        {
            var index = text.IndexOf(FindText, offset, StringComparison.OrdinalIgnoreCase); if (index < 0) break;
            if (FindPointer(segments, index, false) is { } start && FindPointer(segments, index + FindText.Length, true) is { } end) ranges.Add(new(start, end));
            offset = index + FindText.Length;
        }
        foreach (var range in ranges) { range.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Color.FromRgb(107, 81, 33))); range.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White); }
    }
    public bool SelectFind(string query, int occurrence)
    {
        ChatMessageSurface.AllowNavigation(this);
        if (string.IsNullOrEmpty(query)) return false;
        if (_revealing) Render();
        var (text, segments) = SearchDocument(); var offset = 0;
        for (var found = 0; found <= occurrence; found++)
        {
            var index = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase); if (index < 0) return false;
            if (found == occurrence && segments.FirstOrDefault(segment => segment.Embedded is not null && index >= segment.Start && index + query.Length <= segment.Start + segment.Length) is { Embedded: { } content } embeddedSegment)
            {
                content.SelectMatch(index - embeddedSegment.Start, query.Length); return true;
            }
            if (found == occurrence && FindPointer(segments, index, false) is { } start && FindPointer(segments, index + query.Length, true) is { } end)
            {
                Selection.Select(start, end);
                var rect = start.GetCharacterRect(LogicalDirection.Forward); if (!rect.IsEmpty) BringIntoView(rect);
                return true;
            }
            offset = index + query.Length;
        }
        return false;
    }
    private void RequestAnnotation(string text)
    { if (!string.IsNullOrWhiteSpace(text)) RaiseEvent(new AnnotationEventArgs(AnnotationRequestedEvent, text)); }
}

public sealed class VisualizationFollowUpEventArgs(RoutedEvent routedEvent, string prompt) : RoutedEventArgs(routedEvent)
{
    public string Prompt { get; } = prompt;
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget) => ((EventHandler<VisualizationFollowUpEventArgs>)genericHandler)(genericTarget, this);
}

public sealed class AnnotationEventArgs(RoutedEvent routedEvent, string text, string? imagePath = null) : RoutedEventArgs(routedEvent)
{
    public string Text { get; } = text;
    public string? ImagePath { get; } = imagePath;
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget) => ((EventHandler<AnnotationEventArgs>)genericHandler)(genericTarget, this);
}
