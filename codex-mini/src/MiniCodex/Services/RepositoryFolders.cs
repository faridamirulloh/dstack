namespace MiniCodex.Services;

internal sealed class RepositoryFolders
{
    private readonly Dictionary<string, string> _folders = new(FolderPaths.Comparer);

    public string Resolve(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return path;
        var full = FolderPaths.Normalize(path);
        if (_folders.TryGetValue(full, out var resolved)) return resolved;
        resolved = ResolveWorktree(full);
        _folders[full] = resolved;
        return resolved;
    }

    private static string ResolveWorktree(string path)
    {
        try
        {
            for (string? folder = path; folder is not null; folder = Path.GetDirectoryName(folder))
            {
                var git = Path.Combine(folder, ".git");
                if (Directory.Exists(git)) return path;
                if (!File.Exists(git)) continue;
                var pointer = ReadPointer(git);
                if (!pointer.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase)) return path;
                var metadata = Path.GetFullPath(pointer[7..].Trim(), folder);
                var commonFile = Path.Combine(metadata, "commondir");
                if (!File.Exists(commonFile)) return path;
                var common = FolderPaths.Normalize(Path.GetFullPath(ReadPointer(commonFile), metadata));
                // Resolve only verified linked worktrees with a conventional main-repository .git directory.
                if (!Directory.Exists(common) || !FolderPaths.Name(common, "").Equals(".git", StringComparison.OrdinalIgnoreCase)) return path;
                var repository = Path.GetDirectoryName(common);
                return repository is null ? path : FolderPaths.Normalize(Path.GetFullPath(Path.GetRelativePath(folder, path), repository));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return path;
    }

    private static string ReadPointer(string path)
    {
        if (new FileInfo(path).Length > 8192) throw new IOException("Git worktree metadata is too large.");
        return File.ReadAllText(path).Trim();
    }
}
