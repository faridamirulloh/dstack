using MiniCodex.Models;

namespace MiniCodex.Services;

public sealed record DirectoryPage(IReadOnlyList<ComposerReference> Entries, bool HasMore, string Notice);
public sealed record FileMatches(IReadOnlyList<ComposerReference> Entries, string Notice);

public static class ProjectFiles
{
    public const int PageSize = 300;

    private static bool Within(string path, string root) => FolderPaths.Comparer.Equals(path, root)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void Validate(string path, IReadOnlyList<string> roots)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = roots.Where(candidate => Within(full, candidate)).OrderByDescending(candidate => candidate.Length).FirstOrDefault()
            ?? throw new IOException("This item is outside the current project's folders.");
        if (!FolderPaths.Comparer.Equals(FolderPaths.Canonicalize(root), root)) throw new IOException("The project folder moved. Add its current location through Manage folders.");
        for (var current = full; !FolderPaths.Comparer.Equals(current, root); current = Path.GetDirectoryName(current) ?? root)
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Links and junctions are not followed by the project explorer.");
        _ = File.GetAttributes(full);
    }

    public static DirectoryPage List(string path, IReadOnlyList<string> roots, int offset, CancellationToken cancellation)
    {
        var rows = new List<ComposerReference>();
        try
        {
            Validate(path, roots);
            var skipped = 0;
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                cancellation.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (skipped++ < offset) continue;
                if (rows.Count == PageSize) return new(Sort(rows), true, "");
                rows.Add(Reference(entry, roots));
            }
            return new(Sort(rows), false, "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { return new(Sort(rows), false, error.Message); }
    }

    private static ComposerReference[] Sort(List<ComposerReference> rows) => rows.OrderBy(row => row.Kind == ReferenceKind.Folder ? 0 : 1).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    private static ComposerReference Reference(FileSystemInfo entry, IReadOnlyList<string> roots)
    {
        var root = roots.Where(candidate => Within(entry.FullName, candidate)).OrderByDescending(candidate => candidate.Length).First();
        var relative = Path.GetRelativePath(root, entry.FullName);
        return new((entry.Attributes & FileAttributes.Directory) != 0 ? ReferenceKind.Folder : ReferenceKind.File,
            entry.Name, entry.FullName, relative, Source: System.IO.Path.GetFileName(root));
    }

    public static FileMatches Find(IReadOnlyList<string> roots, string query, CancellationToken cancellation, bool explorerSearch = false)
    {
        var matches = new List<ComposerReference>();
        var visited = new HashSet<string>(FolderPaths.Comparer);
        var pending = new Queue<(string Path, int Depth)>();
        var inspected = 0;
        var limited = false;
        var inaccessible = false;
        var maxMatches = explorerSearch ? 200 : 40;
        var maxInspected = explorerSearch ? 50000 : 6000;
        query = query.Replace('/', '\\');
        foreach (var root in roots)
        {
            cancellation.ThrowIfCancellationRequested();
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
            if (name.Length == 0) name = root;
            if (query.Length == 0 || root.Contains(query, StringComparison.OrdinalIgnoreCase))
                matches.Add(new(ReferenceKind.Folder, name, root, "Project root"));
            pending.Enqueue((root, 0));
        }
        while (pending.TryDequeue(out var directory) && matches.Count < maxMatches && inspected < maxInspected)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!visited.Add(directory.Path)) continue;
            try
            {
                Validate(directory.Path, roots);
                foreach (var entry in new DirectoryInfo(directory.Path).EnumerateFileSystemInfos())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++inspected > maxInspected) { limited = true; break; }
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var row = Reference(entry, roots);
                    if (query.Length == 0 || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                        matches.Add(row);
                    if (matches.Count >= maxMatches) { limited = true; break; }
                    if (query.Length > 0 && row.Kind == ReferenceKind.Folder && directory.Depth < 20
                        && (explorerSearch || entry.Name is not (".git" or "node_modules" or "bin" or "obj"))) pending.Enqueue((entry.FullName, directory.Depth + 1));
                    else if (row.Kind == ReferenceKind.Folder && directory.Depth >= 20) limited = true;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { inaccessible = true; }
        }
        limited |= pending.Count > 0 || inspected >= maxInspected;
        return new(matches.Take(maxMatches).ToArray(), limited ? $"Search limited to {maxMatches} matches, {maxInspected:N0} entries, and 20 folder levels. Refine the search or browse the folder tree."
            : inaccessible ? "Some folders are unavailable or cannot be read." : "");
    }
}
