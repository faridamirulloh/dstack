using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Windows.Media.Imaging;
using MiniCodex.Presentation;

namespace MiniCodex.Models;

public sealed class ChatAnnotation(string quote, string comment = "") : ObservableObject
{
    private string _comment = comment;
    public string Quote { get; } = quote;
    public string Comment { get => _comment; set { if (_comment == value) return; _comment = value; Changed(nameof(Comment), nameof(HasComment)); } }
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
    public string Preview => Quote.ReplaceLineEndings(" ") is { } text && text.Length > 90 ? text[..90] + "…" : Quote.ReplaceLineEndings(" ");
}

public sealed class ChatAttachment(string path, bool isImage)
{
    private bool _thumbnailLoaded;
    private BitmapSource? _thumbnail;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public bool IsImage { get; } = isImage;
    public bool IsFile => !IsImage;
    public string Detail => IsImage ? "Image" : "File";
    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded || !IsImage) return _thumbnail;
            _thumbnailLoaded = true;
            try
            {
                if (!File.Exists(Path) || new FileInfo(Path).Length > 25 * 1024 * 1024) return null;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 180; image.UriSource = new Uri(Path, UriKind.Absolute); image.EndInit(); image.Freeze();
                _thumbnail = image;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or FormatException) { }
            return _thumbnail;
        }
    }
}

public sealed record ParsedMessage(string Body, IReadOnlyList<ChatAnnotation> Annotations, IReadOnlyList<ChatAttachment> Attachments);

public static class MessagePayload
{
    private const string AnnotationHeader = "[Mini Codex annotations v1]\n";
    private const string AnnotationEnd = "[/Mini Codex annotations]\n";
    private sealed record AnnotationData(string Quote, string Comment);
    private static readonly JsonSerializerOptions AnnotationJson = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const string QuoteHeader = "[Quoted from Codex]\n";
    private const string AttachmentHeader = "[Attached files and images]\n";
    private const string AttachmentEnd = "[End attachments]\n";
    private static readonly Regex AttachmentLine = new(@"^- \[(?<kind>Image|File): (?<name>[^\r\n]*)\]\((?<path>file:[^\r\n]+)\)$", RegexOptions.NonBacktracking);

    public static string Compose(string body, IReadOnlyList<ChatAnnotation> annotations, IReadOnlyList<ChatAttachment> attachments)
    {
        var text = new StringBuilder();
        if (annotations.Count > 0)
        {
            text.Append(AnnotationHeader);
            text.Append(JsonSerializer.Serialize(annotations.Select(annotation => new AnnotationData(annotation.Quote, annotation.Comment)), AnnotationJson));
            text.Append('\n').Append(AnnotationEnd).Append('\n');
        }
        if (attachments.Count > 0)
        {
            text.Append(AttachmentHeader);
            foreach (var attachment in attachments)
            {
                var name = attachment.Name.Replace("[", "(").Replace("]", ")").ReplaceLineEndings(" ");
                var uri = new Uri(attachment.Path).AbsoluteUri.Replace("(", "%28").Replace(")", "%29");
                text.Append("- [").Append(attachment.IsImage ? "Image: " : "File: ").Append(name).Append("](").Append(uri).AppendLine(")");
            }
            text.Append(AttachmentEnd).AppendLine();
        }
        return text.Append(body).ToString();
    }

    public static void ValidateAnnotations(IReadOnlyList<ChatAnnotation> annotations)
    {
        var serialized = JsonSerializer.Serialize(annotations.Select(annotation => new AnnotationData(annotation.Quote, annotation.Comment)), AnnotationJson);
        if (serialized.Length > 64 * 1024) throw new InvalidOperationException("The quotes and comments are too large. Shorten them before sending (64 KB combined display limit).");
    }

    public static ParsedMessage Parse(string raw)
    {
        var text = raw.ReplaceLineEndings("\n");
        var annotations = new List<ChatAnnotation>();
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith(AnnotationHeader, StringComparison.Ordinal))
        {
            var endMarker = AnnotationEnd.TrimEnd('\n');
            var end = trimmed.IndexOf("\n" + endMarker, AnnotationHeader.Length, StringComparison.Ordinal);
            if (end > AnnotationHeader.Length)
            {
                try
                {
                    var data = JsonSerializer.Deserialize<AnnotationData[]>(trimmed[AnnotationHeader.Length..end].Trim(), AnnotationJson);
                    if (data is { Length: > 0 and <= 8 } && data.All(item => item is not null && item.Quote is not null))
                    {
                        annotations.AddRange(data.Select(item => new ChatAnnotation(item.Quote, item.Comment ?? "")));
                        text = trimmed[(end + 1 + endMarker.Length)..].TrimStart('\n');
                    }
                }
                catch (JsonException) { }
            }
        }
        while (text.StartsWith(QuoteHeader, StringComparison.Ordinal) && annotations.Count < 8)
        {
            var position = QuoteHeader.Length; var quote = new List<string>();
            while (position < text.Length && text[position] == '>')
            {
                var end = text.IndexOf('\n', position); if (end < 0) end = text.Length;
                var line = text[(position + 1)..end]; quote.Add(line.StartsWith(' ') ? line[1..] : line);
                position = end < text.Length ? end + 1 : end;
            }
            if (quote.Count == 0) break;
            annotations.Add(new(string.Join("\n", quote))); text = text[position..].TrimStart('\n');
        }
        var attachments = new List<ChatAttachment>();
        if (text.StartsWith(AttachmentHeader, StringComparison.Ordinal) && text.IndexOf(AttachmentEnd, StringComparison.Ordinal) is var endIndex && endIndex >= 0)
        {
            foreach (var line in text[AttachmentHeader.Length..endIndex].Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(20))
            {
                var match = AttachmentLine.Match(line);
                if (match.Success && Uri.TryCreate(match.Groups["path"].Value, UriKind.Absolute, out var uri) && uri.IsFile)
                    attachments.Add(new(uri.LocalPath, match.Groups["kind"].Value == "Image"));
            }
            text = text[(endIndex + AttachmentEnd.Length)..].TrimStart('\n');
        }
        return new(text, annotations, attachments);
    }
}
