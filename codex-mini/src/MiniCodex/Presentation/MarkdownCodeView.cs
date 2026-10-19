using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace MiniCodex.Presentation;

internal sealed class MarkdownCodeView : TextEditor, IMarkdownSearchContent
{
    private static readonly Dictionary<string, IHighlightingDefinition?> ExtraDefinitions = [];
    private static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);

    internal MarkdownCodeView(string text, string language, bool showHeader)
    {
        Text = text; IsReadOnly = true; Document.UndoStack.SizeLimit = 0;
        FontFamily = new FontFamily("Consolas"); FontSize = 12;
        Background = Brush("CodeSurfaceBrush"); Foreground = Brush("TextBrush");
        BorderThickness = new Thickness(0, showHeader ? 1 : 0, 0, 0); BorderBrush = Brush("LineBrush");
        Padding = new Thickness(10, 8, 10, 8); MaxHeight = showHeader ? 420 : 320;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        TextArea.SelectionBrush = Brush("SelectionBrush"); TextArea.Caret.CaretBrush = Brush("AccentBrush");
        TextArea.TextView.LinkTextForegroundBrush = Brush("AccentBrush");
        SyntaxHighlighting = Highlighting(language);
        var quote = new MenuItem { Header = "Annotate selection" };
        quote.Click += (_, _) => Annotate();
        var copy = new MenuItem { Header = "Copy selection" };
        copy.Click += (_, _) => CopyText(SelectedText);
        ContextMenu = new ContextMenu { Items = { copy, quote } };
        ContextMenu.Opened += (_, _) => copy.IsEnabled = quote.IsEnabled = SelectionLength > 0;
        SelectionAnnotation.Attach(this, () => SelectedText, () =>
        {
            var rectangle = TextArea.Caret.CalculateCaretRectangle();
            if (!rectangle.IsEmpty)
            {
                var origin = TextArea.TextView.TranslatePoint(new Point(0, 0), this);
                rectangle.Offset(origin.X - HorizontalOffset, origin.Y - VerticalOffset);
            }
            return rectangle;
        });
    }

    private void Annotate()
    { if (SelectionLength > 0) RaiseEvent(new AnnotationEventArgs(MarkdownMessageView.AnnotationRequestedEvent, SelectedText)); }

    public string SourceText => Text;
    public void SelectMatch(int offset, int length)
    {
        ChatMessageSurface.AllowNavigation(this);
        Select(offset, length);
        var location = Document.GetLocation(offset); ScrollTo(location.Line, location.Column);
        BringIntoView();
    }

    internal static Block Block(string text, string language, bool showHeader = true)
    {
        var panel = new DockPanel();
        if (showHeader)
        {
            var header = new DockPanel { Margin = new Thickness(10, 6, 8, 6) };
            var copy = CopyButton(text, "Copy code"); DockPanel.SetDock(copy, Dock.Right); header.Children.Add(copy);
            header.Children.Add(new TextBlock { Text = language, Foreground = Brush("CodeLabelBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        }
        panel.Children.Add(new MarkdownCodeView(text, language, showHeader));
        return new BlockUIContainer(new MarkdownCodeCard
        {
            Child = panel, Background = Brush("PanelBrush"), BorderBrush = Brush("LineBrush"),
            BorderThickness = new Thickness(showHeader ? 1 : 0), CornerRadius = new CornerRadius(5)
        }) { Margin = new Thickness(0, showHeader ? 5 : 0, 0, showHeader ? 10 : 0) };
    }

    internal static Button CopyButton(string text, string label)
    {
        var copy = new Button { IsEnabled = text.Length > 0, ToolTip = label, Style = (Style)Application.Current.FindResource("CopyIconButton") };
        AutomationProperties.SetName(copy, label);
        copy.Click += (_, e) =>
        {
            e.Handled = true; CopyFeedback.Reset(copy);
            if (CopyText(text)) CopyFeedback.Show(copy);
        };
        return copy;
    }

    internal static bool CopyText(string text)
    {
        if (text.Length == 0) return false;
        try { Clipboard.SetText(text); return true; }
        catch (ExternalException) { AppDialog.Show("The clipboard is busy. Select the text and try copying again.", "Copy"); }
        return false;
    }

    private static IHighlightingDefinition? Highlighting(string language)
    {
        var name = language.ToLowerInvariant() switch
        {
            "c#" or "cs" or "csharp" => "C#", "c" or "cpp" or "c++" or "h" => "C++",
            "js" or "jsx" or "javascript" or "ts" or "tsx" or "typescript" => "JavaScript",
            "py" or "python" => "Python", "ps" or "ps1" or "powershell" => "PowerShell",
            "html" or "htm" => "HTML", "xml" or "xaml" or "svg" => "XML",
            "json" or "jsonc" => "Json", "css" => "CSS", "sql" => "TSQL", "java" => "Java",
            "diff" or "patch" => "Patch", _ => language
        };
        var manager = HighlightingManager.Instance;
        return manager.HighlightingDefinitions.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? manager.GetDefinitionByExtension("." + language) ?? ExtraHighlighting(language);
    }

    protected override IVisualLineTransformer CreateColorizer(IHighlightingDefinition definition) => new DarkColorizer(definition);

    private sealed class DarkColorizer(IHighlightingDefinition definition) : HighlightingColorizer(definition)
    {
        private readonly Dictionary<HighlightingColor, HighlightingColor> _palette = new(ReferenceEqualityComparer.Instance);
        protected override void ApplyColorToElement(VisualLineElement element, HighlightingColor color)
        {
            if (_palette.TryGetValue(color, out var themed)) { base.ApplyColorToElement(element, themed); return; }
            var token = color.Name?.ToLowerInvariant() ?? "";
            var value = token.Contains("comment") ? "#85968A"
                : token.Contains("string") || token.Contains("char") || token.Contains("attributevalue") ? "#DFC18E"
                : token.Contains("number") || token.Contains("digit") || token.Contains("boolean") ? "#A0C5AE"
                : token.Contains("keyword") || token.Contains("control") || token.Contains("modifier") ? "#CCB7F5"
                : token.Contains("type") || token.Contains("class") || token.Contains("tag") ? "#8FD6D5"
                : token.Contains("method") || token.Contains("function") ? "#B2D2FF"
                : token.Contains("invalid") || token.Contains("error") ? "#E8A6AD" : "#C2C9D8";
            themed = color.Clone();
            themed.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(value));
            themed.Background = null;
            _palette[color] = themed;
            base.ApplyColorToElement(element, themed);
        }
    }

    private static IHighlightingDefinition? ExtraHighlighting(string language)
    {
        var name = language.ToLowerInvariant() switch
        {
            "sh" or "shell" or "bash" or "zsh" => "Shell", "yaml" or "yml" => "YAML",
            "rs" or "rust" => "Rust", "go" or "golang" => "Go", "mermaid" => "Mermaid", _ => ""
        };
        if (name.Length == 0) return null;
        if (ExtraDefinitions.TryGetValue(name, out var existing)) return existing;
        var words = name switch
        {
            "Shell" => "if then else elif fi for while until do done case esac in function select time export local readonly return break continue sudo echo cd printf source set unset",
            "YAML" => "true false null yes no on off",
            "Rust" => "as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait true type unsafe use where while",
            "Go" => "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var true false nil",
            _ => "graph flowchart subgraph end sequenceDiagram participant actor activate deactivate loop alt else opt par and critical option break rect note over stateDiagram classDiagram erDiagram pie title gantt section journey gitGraph mindmap timeline quadrantChart xychart requirementDiagram C4Context direction click style classDef linkStyle"
        };
        var keywords = string.Concat(words.Split(' ').Select(word => "<Word>" + word + "</Word>"));
        var comments = name is "Rust" or "Go" ? "<Span color=\"Comment\" begin=\"/\\*\" end=\"\\*/\"/><Span color=\"Comment\" begin=\"//\"/>"
            : "<Span color=\"Comment\" begin=\"" + (name == "Mermaid" ? "%%" : "#") + "\"/>";
        var extra = name == "YAML" ? "<Rule color=\"Key\">^[ \\t]*[\\w.-]+(?=\\s*:)</Rule>"
            : name == "Shell" ? "<Rule color=\"Keyword\">\\$\\{?[\\w]+\\}?</Rule>" : "";
        var xml = """
            <SyntaxDefinition xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008" name="
            """ + name + """
            "><Color name="Comment" foreground="#85968A"/><Color name="String" foreground="#DFC18E"/>
            <Color name="Number" foreground="#A0C5AE"/><Color name="Keyword" foreground="#CCB7F5"/><Color name="Key" foreground="#8FD6D5"/>
            <RuleSet>
            """ + comments + """
            <Span color="String" begin="&quot;" end="&quot;"><RuleSet><Rule>\\.</Rule></RuleSet></Span>
            <Span color="String" begin="'" end="'"/>
            <Span color="String" begin="\x60" end="\x60"/>
            <Rule color="Number">\b(0x[\da-fA-F]+|\d+(\.\d+)?)\b</Rule>
            """ + extra + "<Keywords color=\"Keyword\">" + keywords + "</Keywords></RuleSet></SyntaxDefinition>";
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml));
            var definition = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(reader, HighlightingManager.Instance);
            ExtraDefinitions[name] = definition;
            return definition;
        }
        catch (HighlightingDefinitionInvalidException error)
        {
            // Keep code readable if a custom definition fails, including on subsequent renders.
            ExtraDefinitions[name] = null;
            System.Diagnostics.Debug.WriteLine($"Syntax highlighting disabled for {name}: {error}");
            return null;
        }
    }
}

internal sealed class MarkdownCodeCard : Border { }

internal interface IMarkdownSearchContent
{
    string SourceText { get; }
    void SelectMatch(int offset, int length);
}
