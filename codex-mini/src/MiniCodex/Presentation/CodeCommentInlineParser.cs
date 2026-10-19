using System.Globalization;
using System.Text;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax.Inlines;

namespace MiniCodex.Presentation;

internal sealed class CodeCommentInline(string title, string body, string file, int? start, int? end, int? priority, string marker) : LeafInline
{
    public string Title { get; } = title;
    public string Body { get; } = body;
    public string File { get; } = file;
    public int? Start { get; } = start;
    public int? End { get; } = end;
    public int? Priority { get; } = priority;
    public string Marker { get; } = marker;
}

internal sealed class CodeCommentInlineParser : InlineParser
{
    private const string Prefix = "::code-comment{";
    public CodeCommentInlineParser() => OpeningCharacters = [':'];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var text = slice.Text.AsSpan(slice.Start, slice.End - slice.Start + 1);
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = Prefix.Length;
        while (position < text.Length)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position >= text.Length) return false;
            if (text[position] == '}')
            {
                if (!attributes.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title)
                    || !attributes.TryGetValue("body", out var body) || string.IsNullOrWhiteSpace(body)) return false;
                var priority = Number(attributes, "priority");
                if (priority is not (>= 0 and <= 3)) priority = null;
                title = title.Trim();
                if (title.Length >= 4 && title.StartsWith("[P", StringComparison.Ordinal) && title[2] is >= '0' and <= '3' && title[3] == ']')
                {
                    priority ??= title[2] - '0';
                    title = title[4..].TrimStart();
                }
                if (title.Length == 0) return false;
                var start = Number(attributes, "start");
                if (start is not > 0) start = null;
                var end = Number(attributes, "end");
                if (start is null || end is null || end < start) end = null;
                var length = position + 1;
                processor.Inline = new CodeCommentInline(title, body, attributes.GetValueOrDefault("file") ?? "",
                    start, end, priority, text[..length].ToString());
                slice.Start += length;
                return true;
            }

            var keyStart = position;
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or '-')) position++;
            if (position == keyStart) return false;
            var key = text[keyStart..position].ToString();
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position >= text.Length || text[position++] != '=') return false;
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position >= text.Length) return false;
            string value;
            if (text[position] is '"' or '\'')
            {
                var quote = text[position++];
                var content = new StringBuilder();
                while (position < text.Length && text[position] != quote)
                {
                    var character = text[position++];
                    if (character == '\\' && position < text.Length)
                    {
                        var escaped = text[position];
                        if (escaped == quote || escaped == '\\') { content.Append(escaped); position++; continue; }
                        if (key != "file" && escaped is 'n' or 'r' or 't')
                        {
                            content.Append(escaped switch { 'n' => '\n', 'r' => '\r', _ => '\t' });
                            position++; continue;
                        }
                    }
                    content.Append(character);
                }
                if (position >= text.Length) return false;
                position++;
                value = content.ToString();
            }
            else
            {
                var valueStart = position;
                while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] != '}') position++;
                if (position == valueStart) return false;
                value = text[valueStart..position].ToString();
            }
            if (!attributes.TryAdd(key, value)) return false;
            if (position < text.Length && text[position] != '}' && !char.IsWhiteSpace(text[position])) return false;
        }
        return false;
    }

    private static int? Number(Dictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
