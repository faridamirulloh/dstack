using System.Security.Cryptography;
using System.Text.Json;

namespace MiniCodex.Services;

internal static class GeneratedImageStore
{
    private const int MaxBytes = 25 * 1024 * 1024;

    public static string? Resolve(string? savedPath, string? result)
    {
        var local = LocalPath(savedPath);
        if (local is not null && File.Exists(local)) return local;
        var resultPath = LocalPath(result);
        if (resultPath is not null && File.Exists(resultPath)) return resultPath;
        local ??= resultPath;
        if (string.IsNullOrWhiteSpace(result) || result.Length > (MaxBytes + 2) / 3 * 4 + 256) return local;
        try
        {
            var encoded = result.AsSpan();
            if (encoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = encoded.IndexOf(',');
                if (comma < 0 || !encoded[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return local;
                encoded = encoded[(comma + 1)..];
            }
            var buffer = new byte[encoded.Length / 4 * 3 + 3];
            if (!Convert.TryFromBase64Chars(encoded, buffer, out var count) || count > MaxBytes) return local;
            var bytes = buffer.AsSpan(0, count);
            var extension = Extension(bytes);
            if (extension is null) return local;
            return Save(bytes, extension);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return local; }
    }

    public static string? Resolve(JsonElement value, string? savedPath = null)
    {
        var local = LocalPath(savedPath);
        if (local is not null && File.Exists(local)) return local;
        if (value.ValueKind != JsonValueKind.String) return local;
        // Decode ordinary base64 directly from UTF-8 JSON instead of copying it to a large UTF-16 string.
        if (value.TryGetBytesFromBase64(out var bytes))
        {
            if (bytes.Length > MaxBytes || Extension(bytes) is not { } extension) return local;
            try { return Save(bytes, extension); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return local; }
        }
        var text = value.GetString();
        var resolved = Resolve(savedPath, text);
        if (resolved is not null) return resolved;
        return text is not null && (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(text, UriKind.Absolute, out _) ? text : null;
    }

    private static string Save(ReadOnlySpan<byte> bytes, string extension)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCodex", "generated-images");
        var path = Path.Combine(folder, "generated-" + hash + extension);
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(folder);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }

    public static string Markdown(string path)
    {
        var uri = new Uri(path, UriKind.Absolute).AbsoluteUri.Replace("(", "%28").Replace(")", "%29");
        return "![Generated image](" + uri + ")";
    }

    private static string? LocalPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32767) return null;
        try
        {
            if (Path.IsPathFullyQualified(value)) return Path.GetFullPath(value);
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile ? Path.GetFullPath(uri.LocalPath) : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    private static string? Extension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.StartsWith(new byte[] { 255, 216, 255 })) return ".jpg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return ".gif";
        if (bytes.StartsWith("BM"u8)) return ".bmp";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }
}
