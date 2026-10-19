using System.Security.Cryptography;
using System.Text;

namespace MiniCodex.Services;

public sealed record CodexInstructionsSnapshot(string FilePath, string Text, string Revision, bool IsOverride);

// Edit the same user-owned guidance files that Codex discovers in CODEX_HOME.
public sealed class CodexInstructionsStore(string home)
{
    public const int MaxBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public string HomePath { get; } = Path.GetFullPath(home);
    private static byte[]? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxBytes) throw new IOException("This instructions file exceeds the editor's 1 MiB limit.");
            using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            if (bytes.Length > MaxBytes) throw new IOException("This instructions file exceeds the editor's 1 MiB limit.");
            return bytes.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private static string Decode(byte[]? bytes)
    {
        if (bytes is null) return "";
        try { return Utf8.GetString(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? bytes.AsSpan(3) : bytes); }
        catch (DecoderFallbackException error) { throw new IOException("The instructions file is not valid UTF-8. Open it in a text editor and save it as UTF-8.", error); }
    }
    private static string Hash(byte[]? bytes) => bytes is null ? "missing" : Convert.ToHexString(SHA256.HashData(bytes));
    public CodexInstructionsSnapshot Load()
    {
        var standard = Read(Path.Combine(HomePath, "AGENTS.md"));
        var temporary = Read(Path.Combine(HomePath, "AGENTS.override.md"));
        var overriding = Decode(temporary);
        var isOverride = !string.IsNullOrWhiteSpace(overriding);
        return new(Path.Combine(HomePath, isOverride ? "AGENTS.override.md" : "AGENTS.md"), isOverride ? overriding : Decode(standard),
            Hash(standard) + ":" + Hash(temporary), isOverride);
    }
    public CodexInstructionsSnapshot Save(CodexInstructionsSnapshot original, string text)
    {
        if (Utf8.GetByteCount(text) > MaxBytes) throw new IOException("Keep instructions within the editor's 1 MiB limit.");
        var current = Load();
        if (current.Revision != original.Revision || !FolderPaths.Comparer.Equals(current.FilePath, original.FilePath))
            throw new IOException("These instructions changed in another window or app. Copy your edits, then use Reload before saving.");
        if (text == current.Text) return current;
        if (File.Exists(current.FilePath) && File.GetAttributes(current.FilePath).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("This instructions file is a link. Edit its target in a text editor to preserve the link.");
        Directory.CreateDirectory(HomePath);
        var temporary = Path.Combine(HomePath, ".instructions-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(Utf8.GetBytes(text)); stream.Flush(true); }
            // Recheck after writing the temporary file; stale editors never overwrite a newer save.
            if (Load().Revision != original.Revision) throw new IOException("These instructions changed while saving. Copy your edits, then use Reload.");
            if (File.Exists(current.FilePath)) File.Replace(temporary, current.FilePath, null);
            else File.Move(temporary, current.FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Load();
    }
}
