using MiniCodex.Presentation;
using MiniCodex.Codex;
using System.Text.Json.Serialization;

namespace MiniCodex.Models;

public sealed record AttachmentReference(string Path, bool IsImage)
{
    [JsonIgnore] public string Name => System.IO.Path.GetFileName(Path);
    public ChatAttachment Open() => new(Path, IsImage);
}

public sealed class QueuedMessage : ObservableObject
{
    private string _text = "";
    private ParsedMessage? _parsed;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get => _text; set { _text = value; _parsed = null; Changed(nameof(Text), nameof(Body), nameof(Annotations), nameof(Preview), nameof(Settings)); } }
    public string ModelId { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string Effort { get; set; } = "medium";
    public string ApprovalMode { get; set; } = "Ask me";
    public bool FastMode { get; set; }
    public bool MayHaveBeenSent { get; set; }
    public ComposerReference[] References { get; set; } = [];
    public AttachmentReference[] Attachments { get; set; } = [];
    private ParsedMessage Parsed => _parsed ??= MessagePayload.Parse(Text);
    [JsonIgnore] public string Body => Parsed.Body;
    [JsonIgnore] public IReadOnlyList<ChatAnnotation> Annotations => Parsed.Annotations;
    public string Preview => Wire.Short(Body.ReplaceLineEndings(" "), 140) is { Length: > 0 } body ? body : "Annotations or attachments";
    public string Settings => ModelName + " · " + Effort + (FastMode ? " · Fast" : " · Standard") + " · " + ApprovalMode;
}
