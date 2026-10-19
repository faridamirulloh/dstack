using System.Text.Json;

namespace MiniCodex.Codex;

public static class Wire
{
    public static JsonElement Get(this JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found) ? found : default;
    public static string? Text(this JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static string? Text(this JsonElement value, string name) => value.Get(name).Text();
    public static long? Number(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    public static IEnumerable<JsonElement> Items(this JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    public static string Short(string? text, int limit = 2000) => string.IsNullOrEmpty(text) ? "" : text.Length <= limit ? text : text[..limit] + "\n[truncated]";
}

public sealed record WireMessage(string Method, JsonElement Parameters, JsonElement Id, int ByteSize);
public sealed class CodexRpcException(int code, string message) : IOException(message) { public int Code { get; } = code; }
public sealed class CodexPayloadTooLargeException() : IOException("This Codex response is too large. The account remains connected.") { }
