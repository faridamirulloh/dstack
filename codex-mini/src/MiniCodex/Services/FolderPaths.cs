using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MiniCodex.Services;

public static class FolderPaths
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    public static string DefaultChatFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Codex");

    public static bool IsDefaultChatFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try { return ContainsFolder(DefaultChatFolder, path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static bool SameFolder(string left, string right) => Comparer.Equals(Normalize(left), Normalize(right));

    public static bool ContainsFolder(string root, string path)
    {
        var parent = Normalize(root);
        var child = Normalize(path);
        return parent.Length > 0 && (Comparer.Equals(parent, child)
            || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar))); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    public static string Name(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path)) return fallback;
        var folder = Path.TrimEndingDirectorySeparator(path);
        return Path.GetFileName(folder) is { Length: > 0 } name ? name : folder;
    }

    public static string Canonicalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new IOException($"Folder is unavailable: {fullPath}");

        using var handle = CreateFile(fullPath, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Cannot access folder: {fullPath}", new Win32Exception(Marshal.GetLastWin32Error()));

        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer.Capacity = checked((int)length + 1);
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException($"Cannot resolve folder: {fullPath}", new Win32Exception(Marshal.GetLastWin32Error()));

        var resolved = buffer.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = @"\\" + resolved[8..];
        else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        return Path.TrimEndingDirectorySeparator(resolved);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
