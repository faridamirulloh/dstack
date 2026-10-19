using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax.Inlines;
using MiniCodex.Models;
using Md = Markdig.Syntax;
using MdTable = Markdig.Extensions.Tables.Table;
using MdRow = Markdig.Extensions.Tables.TableRow;
using MdCell = Markdig.Extensions.Tables.TableCell;
using Table = System.Windows.Documents.Table;
using TableRow = System.Windows.Documents.TableRow;
using TableCell = System.Windows.Documents.TableCell;

namespace MiniCodex.Presentation;

internal sealed class MarkdownDocument
{
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();
    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables(new PipeTableOptions { UseHeaderForColumnCount = true })
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseTaskLists().UseFootnotes().UseMathematics().UseAutoLinks().UseSoftlineBreakAsHardlineBreak().DisableHtml();
        builder.InlineParsers.Insert(0, new LatexInlineParser());
        builder.InlineParsers.Insert(0, new VisualizationInlineParser());
        return builder.Build();
    }
    private readonly string _workingDirectory;
    private readonly IReadOnlyList<ComposerReference> _mentions;
    private readonly Dictionary<Footnote, Section> _footnotes = [];
    private readonly Dictionary<int, Hyperlink> _footnoteReferences = [];
    private bool _compactLists;
    private bool _allowVisualizations;

    private MarkdownDocument(string workingDirectory, IReadOnlyList<ComposerReference> mentions)
    { _workingDirectory = workingDirectory; _mentions = mentions; }
    private static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);

    public static FlowDocument Create(string text, string role, string workingDirectory, IReadOnlyList<ComposerReference>? mentions = null) =>
        new MarkdownDocument(workingDirectory, mentions ?? []).Render(text, role);

    private FlowDocument Render(string text, string role)
    {
        var document = new FlowDocument { FontSize = 13.5, LineHeight = 22, PagePadding = new Thickness(0), Foreground = Brush("TextBrush") };
        document.SetResourceReference(FlowDocument.FontFamilyProperty, SystemFonts.MessageFontFamilyKey);
        document.SetResourceReference(FlowDocument.FontStyleProperty, SystemFonts.MessageFontStyleKey);
        document.SetResourceReference(FlowDocument.FontWeightProperty, SystemFonts.MessageFontWeightKey);
        if (role == "Command") { document.Blocks.Add(CodeBlock(text, "Terminal", false)); return document; }
        if (role == "Files") { AddFiles(document, text); return document; }
        if (role == "Status")
        {
            document.FontSize = 12; document.LineHeight = 18;
            document.Foreground = text.StartsWith("Task completed", StringComparison.OrdinalIgnoreCase) ? Brush("GoodBrush")
                : new[] { "failed", "error", "lost", "unknown" }.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)) ? Brush("WarningBrush") : Brush("MutedBrush");
        }
        _compactLists = role == "You";
        _allowVisualizations = role == "Codex";
        // Parse the whole reply so later reference definitions and footnotes resolve earlier links.
        // The message model bounds text size; formatting is never discarded after a block/run count.
        AddBlocks(document.Blocks, Markdown.Parse(text, Pipeline));
        return document;
    }

    private void AddBlocks(BlockCollection target, Md.ContainerBlock source, int listDepth = 0)
    {
        foreach (var block in source)
        {
            switch (block)
            {
                case Md.LinkReferenceDefinition:
                    break;
                case MathBlock math:
                    target.Add(new BlockUIContainer(MarkdownVisuals.Formula(math.Lines.ToString(), true))
                    { Margin = new Thickness(0, 8, 0, 12) });
                    break;
                case Md.FencedCodeBlock fenced:
                    var language = fenced.Info?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    var code = fenced.Lines.ToString();
                    target.Add(language.Equals("mermaid", StringComparison.OrdinalIgnoreCase)
                        ? MarkdownVisuals.Diagram(code)
                        : language is "math" or "latex" or "tex"
                            ? new BlockUIContainer(MarkdownVisuals.Formula(code, true)) { Margin = new Thickness(0, 8, 0, 12) }
                            : CodeBlock(code, language.Length == 0 ? "Code" : language));
                    break;
                case Md.CodeBlock codeBlock:
                    target.Add(CodeBlock(codeBlock.Lines.ToString(), "Code"));
                    break;
                case Md.HeadingBlock heading:
                    var title = Paragraph(heading.Inline);
                    title.FontSize = heading.Level switch { 1 => 20, 2 => 17, 3 => 15, _ => 14 };
                    title.LineHeight = title.FontSize * 1.45; title.FontWeight = FontWeights.SemiBold;
                    title.Foreground = Brush("ReplyHeadingBrush"); title.Margin = new Thickness(0, 12, 0, 8);
                    target.Add(title);
                    break;
                case Md.ParagraphBlock paragraph:
                    target.Add(Paragraph(paragraph.Inline));
                    break;
                case Md.ThematicBreakBlock:
                    target.Add(new Paragraph { BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 12) });
                    break;
                case Md.QuoteBlock quote:
                    var section = new Section
                    {
                        BorderBrush = Brush("AccentBrush"), BorderThickness = new Thickness(2, 0, 0, 0),
                        Padding = new Thickness(10, 2, 0, 2), Margin = new Thickness(0, 5, 0, 9), Foreground = Brush("MutedBrush")
                    };
                    AddBlocks(section.Blocks, quote, listDepth);
                    target.Add(section);
                    break;
                case Md.ListBlock list:
                    target.Add(ReadList(list, listDepth));
                    break;
                case MdTable table:
                    target.Add(ReadTable(table, listDepth));
                    break;
                case FootnoteGroup notes:
                    var noteSection = new Section { BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0), Margin = new Thickness(0, 12, 0, 0), FontSize = 12 };
                    foreach (var note in notes.OfType<Footnote>())
                    {
                        var entry = new Section { Margin = new Thickness(0, 0, 0, 6) };
                        _footnotes[note] = entry;
                        entry.Blocks.Add(new Paragraph(new Run("[" + note.Order.ToString(CultureInfo.InvariantCulture) + "]"))
                        { FontWeight = FontWeights.SemiBold, Foreground = Brush("AccentBrush"), Margin = new Thickness(0, 0, 0, 3) });
                        AddBlocks(entry.Blocks, note, listDepth);
                        noteSection.Blocks.Add(entry);
                    }
                    target.Add(noteSection);
                    break;
                case Md.ContainerBlock container:
                    AddBlocks(target, container, listDepth);
                    break;
                case Md.LeafBlock leaf when leaf.Inline is not null:
                    target.Add(Paragraph(leaf.Inline));
                    break;
                case Md.LeafBlock leaf:
                    target.Add(new Paragraph(new Run(leaf.Lines.ToString())) { Margin = new Thickness(0, 0, 0, 7) });
                    break;
            }
        }
    }

    private System.Windows.Documents.List ReadList(Md.ListBlock source, int depth)
    {
        var list = new System.Windows.Documents.List
        {
            MarkerStyle = source.IsOrdered ? TextMarkerStyle.Decimal : depth % 2 == 0 ? TextMarkerStyle.Disc : TextMarkerStyle.Circle,
            MarkerOffset = _compactLists ? 4 : 6,
            Padding = new Thickness(_compactLists ? 12 : 28, 0, 0, 0),
            Margin = new Thickness(0, 2, 0, depth == 0 ? 10 : 4)
        };
        if (source.IsOrdered && int.TryParse(source.OrderedStart, out var start) && start > 0) list.StartIndex = start;
        foreach (var child in source.OfType<Md.ListItemBlock>())
        {
            var item = new ListItem { Margin = new Thickness(0, 0, 0, source.IsLoose ? 6 : 3) };
            AddBlocks(item.Blocks, child, depth + 1);
            foreach (var paragraph in item.Blocks.OfType<Paragraph>()) paragraph.Margin = new Thickness(0, 0, 0, source.IsLoose ? 7 : 4);
            if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph());
            list.ListItems.Add(item);
        }
        if (source.IsOrdered)
        {
            var digits = ((long)list.StartIndex + Math.Max(0, list.ListItems.Count - 1)).ToString(CultureInfo.InvariantCulture).Length;
            list.Padding = new Thickness(Math.Max(_compactLists ? 18 : 28, digits * (_compactLists ? 8 : 9) + (_compactLists ? 10 : 14)), 0, 0, 0);
        }
        return list;
    }

    private Table ReadTable(MdTable source, int listDepth)
    {
        var table = new Table
        {
            CellSpacing = 0, Margin = new Thickness(0, 8, 0, 12), FontSize = 13, LineHeight = 21,
            BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1, 1, 0, 0)
        };
        var rows = source.OfType<MdRow>().ToArray();
        var columns = Math.Max(source.ColumnDefinitions.Count, rows.Select(row => row.OfType<MdCell>().Sum(cell => cell.ColumnSpan)).DefaultIfEmpty(0).Max());
        for (var index = 0; index < columns; index++) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var group = new TableRowGroup(); table.RowGroups.Add(group);
        foreach (var sourceRow in rows)
        {
            var row = new TableRow();
            var background = Brush(sourceRow.IsHeader ? "RaisedBrush" : group.Rows.Count % 2 == 1 ? "CodeSurfaceBrush" : "SidebarBrush");
            var column = 0;
            foreach (var sourceCell in sourceRow.OfType<MdCell>())
            {
                var alignment = column < source.ColumnDefinitions.Count ? source.ColumnDefinitions[column].Alignment : null;
                var cell = new TableCell
                {
                    Padding = new Thickness(10, 8, 10, 8), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = background, Foreground = Brush(sourceRow.IsHeader ? "ReplyHeadingBrush" : "TextBrush"),
                    FontWeight = sourceRow.IsHeader ? FontWeights.SemiBold : FontWeights.Normal,
                    ColumnSpan = Math.Max(1, sourceCell.ColumnSpan), RowSpan = Math.Max(1, sourceCell.RowSpan),
                    TextAlignment = alignment == TableColumnAlign.Center ? TextAlignment.Center : alignment == TableColumnAlign.Right ? TextAlignment.Right : TextAlignment.Left
                };
                AddBlocks(cell.Blocks, sourceCell, listDepth);
                foreach (var paragraph in cell.Blocks.OfType<Paragraph>()) paragraph.Margin = new Thickness(0);
                row.Cells.Add(cell); column += cell.ColumnSpan;
            }
            group.Rows.Add(row);
        }
        return table;
    }

    // Streaming only needs to recognize a completed delimiter row. Strip container prefixes
    // here; the complete document still goes through the same parser with its nesting intact.
    internal static bool IsTableHeader(string header, string divider)
    {
        if (!header.Contains('|') || !divider.Contains('|')) return false;
        static string Unwrap(string line)
        {
            var text = line.Trim();
            while (text.StartsWith('>')) text = text[1..].TrimStart();
            return text;
        }
        return Markdown.Parse(Unwrap(header) + "\n" + Unwrap(divider), Pipeline).Any(block => block is MdTable);
    }

    private Paragraph Paragraph(ContainerInline? source)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 7) };
        if (source is not null) AddInlines(paragraph.Inlines, source);
        return paragraph;
    }

    private void AddInlines(InlineCollection target, ContainerInline source)
    {
        for (var node = source.FirstChild; node is not null; node = node.NextSibling)
        {
            switch (node)
            {
                case VisualizationInline visualization:
                    target.Add(_allowVisualizations
                        ? new InlineUIContainer(new VisualizationPreview(visualization.Path, visualization.Title, _workingDirectory)) { BaselineAlignment = BaselineAlignment.Center }
                        : new Run(visualization.Marker));
                    break;
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case HtmlEntityInline entity:
                    target.Add(new Run(entity.Transcoded.ToString()));
                    break;
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = new FontFamily("Consolas"), FontSize = 12.5, Background = Brush("CodeSurfaceBrush"), Foreground = Brush("AccentBrush") });
                    break;
                case LineBreakInline line:
                    if (line.IsHard) target.Add(new LineBreak()); else target.Add(new Run(" "));
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.SemiBold;
                    else span.FontStyle = FontStyles.Italic;
                    AddInlines(span.Inlines, emphasis); target.Add(span);
                    break;
                case LinkInline link:
                    AddLink(target, link.Url ?? "", PlainText(link), link, link.IsImage, link.Title);
                    break;
                case AutolinkInline link:
                    AddLink(target, link.IsEmail ? "mailto:" + link.Url : link.Url, link.Url);
                    break;
                case MathInline math:
                    target.Add(new InlineUIContainer(MarkdownVisuals.Formula(math.Content.ToString(), math.DelimiterCount > 1)) { BaselineAlignment = BaselineAlignment.Center });
                    break;
                case TaskList task:
                    target.Add(new Run(task.Checked ? "☑ " : "☐ ") { Foreground = Brush(task.Checked ? "GoodBrush" : "MutedBrush") });
                    break;
                case FootnoteLink footnote:
                    var reference = new Hyperlink(new Run(footnote.IsBackLink ? "↩" : "[" + footnote.Footnote.Order.ToString(CultureInfo.InvariantCulture) + "]"))
                    { Foreground = Brush("AccentBrush"), FontSize = 10, BaselineAlignment = BaselineAlignment.Superscript, ToolTip = FootnoteText(footnote.Footnote) };
                    if (!footnote.IsBackLink) _footnoteReferences[footnote.Index] = reference;
                    reference.Click += (_, e) =>
                    {
                        e.Handled = true;
                        ChatMessageSurface.AllowNavigation(reference);
                        if (footnote.IsBackLink) { if (_footnoteReferences.TryGetValue(footnote.Index, out var origin)) origin.BringIntoView(); }
                        else if (_footnotes.TryGetValue(footnote.Footnote, out var note)) note.BringIntoView();
                    };
                    target.Add(reference);
                    break;
                case ContainerInline container:
                    AddInlines(target, container);
                    break;
            }
        }
    }

    private static string PlainText(ContainerInline source)
    {
        var text = new StringBuilder();
        for (var node = source.FirstChild; node is not null; node = node.NextSibling)
            text.Append(node switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                HtmlEntityInline entity => entity.Transcoded.ToString(),
                AutolinkInline link => link.Url,
                MathInline math => math.Content.ToString(),
                VisualizationInline visualization => visualization.Title,
                LineBreakInline => " ",
                ContainerInline container => PlainText(container),
                _ => ""
            });
        return text.ToString();
    }
    private static string FootnoteText(Footnote note) =>
        string.Join("\n", note.OfType<Md.LeafBlock>().Select(block => block.Inline is { } inline ? PlainText(inline) : block.Lines.ToString()));

    private void AddLink(InlineCollection targetInlines, string target, string label, ContainerInline? content = null, bool image = false, string? title = null)
    {
        if (image)
        {
            if (FileLinks.TryResolve(target, _workingDirectory, out var imagePath, true)) { targetInlines.Add(ImagePreview.Create(imagePath, label)); return; }
            if (Uri.TryCreate(target, UriKind.Absolute, out var remoteImage) && remoteImage.Scheme is "https" or "http")
            { targetInlines.Add(ImagePreview.CreateRemote(remoteImage, label)); return; }
        }
        var mention = _mentions.FirstOrDefault(entry => string.Equals(entry.Path, Decode(target), StringComparison.OrdinalIgnoreCase)
            || Uri.TryCreate(target, UriKind.Absolute, out var local) && local.IsFile && string.Equals(entry.Path, local.LocalPath, StringComparison.OrdinalIgnoreCase));
        var isMention = label.StartsWith('/') || label.StartsWith('$') || label.StartsWith('@') || target.StartsWith("plugin://", StringComparison.Ordinal) || target.StartsWith("app://", StringComparison.Ordinal);
        if (isMention)
        {
            var kind = mention?.Kind ?? (target.StartsWith("plugin://", StringComparison.Ordinal) ? ReferenceKind.Plugin : target.StartsWith("app://", StringComparison.Ordinal) ? ReferenceKind.Connector : (label.StartsWith('/') || label.StartsWith('$')) ? ReferenceKind.Skill : ReferenceKind.File);
            if (mention is null && kind == ReferenceKind.File && FileLinks.TryResolve(target, _workingDirectory, out var candidate) && Directory.Exists(candidate)) kind = ReferenceKind.Folder;
            var color = kind switch { ReferenceKind.Plugin => "PluginMentionBrush", ReferenceKind.Skill => "SkillMentionBrush", ReferenceKind.Connector => "ConnectorMentionBrush", ReferenceKind.Folder => "FolderMentionBrush", _ => "FileLinkBrush" };
            var name = (mention?.Name ?? label.TrimStart('/', '$', '@')).Split(':').Last();
            if ((kind is ReferenceKind.File or ReferenceKind.Folder) && FileLinks.TryResolve(target, _workingDirectory, out var file))
            {
                var link = FileLinks.Link(file, true, name); link.Foreground = Brush(color); targetInlines.Add(link);
            }
            else
            {
                var tooltip = mention?.Description is { Length: > 0 } description ? description : kind + ": " + name;
                targetInlines.Add(new Run(name) { Foreground = Brush(color), Background = Brush("RaisedBrush"), ToolTip = new TextBlock { Text = tooltip, MaxWidth = 430, TextWrapping = TextWrapping.Wrap } });
            }
        }
        else if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto")
        {
            var link = new Hyperlink { NavigateUri = uri, Foreground = Brush("AccentBrush"), ToolTip = string.IsNullOrWhiteSpace(title) ? uri.AbsoluteUri : title };
            if (content is null) link.Inlines.Add(new Run(label)); else AddInlines(link.Inlines, content);
            link.RequestNavigate += OpenLink; targetInlines.Add(link);
        }
        else if (FileLinks.TryResolve(target, _workingDirectory, out var path, true)) targetInlines.Add(FileLinks.Link(path));
        else targetInlines.Add(new Run(label) { Foreground = Brush("MutedBrush"), ToolTip = target });
    }

    private static Block CodeBlock(string text, string language, bool showHeader = true) =>
        MarkdownCodeView.Block(text, language, showHeader);

    private void AddFiles(FlowDocument document, string text)
    {
        var files = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).Where(line => line.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < files.Length; index++)
        {
            var raw = files[index];
            var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 3) };
            if (FileLinks.TryResolve(raw, _workingDirectory, out var path))
            {
                paragraph.Inlines.Add(FileLinks.Link(path));
                var explore = new Button
                {
                    Content = "\uE8B7", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11,
                    Style = (Style)Application.Current.FindResource("QuietButton"), Padding = new Thickness(4, 2, 4, 2),
                    MinHeight = 22, Width = 25, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Show in Explorer"
                };
                AutomationProperties.SetName(explore, "Show in Explorer");
                explore.Click += (_, _) => FileLinks.ShowInExplorer(path);
                paragraph.Inlines.Add(new InlineUIContainer(explore) { BaselineAlignment = BaselineAlignment.Center });
            }
            else paragraph.Inlines.Add(new Run(FileLinks.Name(raw)) { Foreground = Brush("MutedBrush"), ToolTip = raw });
            document.Blocks.Add(paragraph);
        }
    }

    private static string Decode(string target)
    {
        try { return Uri.UnescapeDataString(target); }
        catch (UriFormatException) { return target; }
    }
    private static void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri.Scheme is not ("https" or "http" or "mailto")) return;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { AppDialog.Show(error.Message, "Could not open link"); }
    }
}
