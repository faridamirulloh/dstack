using System.Text.Json;
using MiniCodex.Services;

namespace MiniCodex.Codex;

internal static class ImagePayloads
{
    private sealed class LimitedStream : MemoryStream
    {
        private void Check(int count) { if (Position + count > 8 * 1024 * 1024) throw new CodexPayloadTooLargeException(); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    }

    public static (JsonElement Value, int ByteSize) Normalize(JsonElement value)
    {
        using var stream = new LimitedStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value);
        using var document = JsonDocument.Parse(stream.GetBuffer().AsMemory(0, checked((int)stream.Length)));
        return (document.RootElement.Clone(), checked((int)stream.Length));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) Write(writer, item);
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            var type = value.Text("type");
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                var image = type == "imageGeneration" && property.Name == "result"
                    || type == "image" && property.Name == "data"
                    || property.Name is "imageUrl" or "image_url" or "b64_json"
                    || property.Name == "blob" && value.Text("mimeType")?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
                if (image && property.Value.ValueKind == JsonValueKind.String)
                    writer.WriteStringValue(GeneratedImageStore.Resolve(property.Value, type == "imageGeneration" ? value.Text("savedPath") : null) ?? "");
                else Write(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else value.WriteTo(writer);
    }
}
