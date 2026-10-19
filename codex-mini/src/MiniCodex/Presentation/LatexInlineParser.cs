using Markdig.Extensions.Mathematics;
using Markdig.Helpers;
using Markdig.Parsers;

namespace MiniCodex.Presentation;

// Dollar delimiters come from Markdig. Recognize the other common LaTeX delimiters
// before Markdown consumes their backslashes as escapes. Code spans remain opaque.
internal sealed class LatexInlineParser : InlineParser
{
    public LatexInlineParser() => OpeningCharacters = ['\\'];
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.Start >= slice.End || slice.CurrentChar != '\\' || slice.Text[slice.Start + 1] is not ('(' or '[')) return false;
        var display = slice.Text[slice.Start + 1] == '[';
        var closing = display ? "\\]" : "\\)";
        var start = slice.Start + 2;
        var end = start;
        while (end < slice.End)
        {
            if (slice.Text[end] == '\\')
            {
                if (slice.Text[end + 1] == closing[1])
                {
                    processor.Inline = new MathInline
                    {
                        Delimiter = '$', DelimiterCount = display ? 2 : 1,
                        Content = new StringSlice(slice.Text, start, end - 1)
                    };
                    slice.Start = end + 2;
                    return true;
                }
                if (slice.Text[end + 1] == '\\') end++;
            }
            end++;
        }
        return false;
    }
}
