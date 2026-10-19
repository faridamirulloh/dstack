using System.Text.Json;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax.Inlines;

namespace MiniCodex.Presentation;

internal sealed class VisualizationInline(string path, string title, string marker) : LeafInline
{
    public string Path { get; } = path;
    public string Title { get; } = title;
    public string Marker { get; } = marker;
}

internal sealed class VisualizationInlineParser : InlineParser
{
    private const string Prefix = "visualize";
    public VisualizationInlineParser() => OpeningCharacters = [''];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var remaining = slice.Text.AsSpan(slice.Start, slice.End - slice.Start + 1);
        if (!remaining.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var end = remaining[..Math.Min(remaining.Length, 16_384)].IndexOf('');
        if (end < Prefix.Length) return false;
        try
        {
            using var json = JsonDocument.Parse(remaining[Prefix.Length..end].ToString());
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("path", out var path)
                || path.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(path.GetString())) return false;
            var title = root.TryGetProperty("title", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            processor.Inline = new VisualizationInline(path.GetString()!, string.IsNullOrWhiteSpace(title) ? "Visualization" : title,
                remaining[..(end + 1)].ToString());
            slice.Start += end + 1;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
