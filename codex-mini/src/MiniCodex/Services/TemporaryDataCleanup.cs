namespace MiniCodex.Services;

internal sealed record TemporaryDataFile(string Path, long Bytes, DateTime Updated);
internal sealed record TemporaryDataBatch(string Category, string? LockPath, IReadOnlyList<TemporaryDataFile> Files, IReadOnlyList<string> Directories);
internal sealed record TemporaryDataSnapshot(string Home, IReadOnlyList<TemporaryDataBatch> Batches, IReadOnlyList<string> Notes)
{
    public long Bytes => Batches.Sum(batch => batch.Files.Sum(file => file.Bytes));
    public int FileCount => Batches.Sum(batch => batch.Files.Count);
}
internal sealed record TemporaryDataResult(int Deleted, long Bytes, int Skipped, IReadOnlyList<string> Notes);

internal static class TemporaryDataCleanup
{
    private const int MaxEntries = 50_000;
    private const int MaxDepth = 20;

    public static TemporaryDataSnapshot Scan(string homePath, CancellationToken cancellation)
    {
        var home = Path.GetFullPath(homePath);
        var batches = new List<TemporaryDataBatch>();
        var notes = new List<string>();
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var remaining = MaxEntries;
        if (!Directory.Exists(home)) return new(home, batches, notes);
        try
        {
            EnsurePlainPath(home, home);
            var log = Path.Combine(home, "log");
            if (Directory.Exists(log))
            {
                try
                {
                    EnsurePlainPath(home, log);
                    var files = new List<TemporaryDataFile>();
                    foreach (var file in Directory.EnumerateFiles(log))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (--remaining < 0) throw new IOException("The scan reached 50,000 entries. Some files were not included.");
                        var name = Path.GetFileName(file);
                        if (!name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !name.Contains(".log.", StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            EnsurePlainPath(home, file);
                            var info = new FileInfo(file);
                            if (info.LastWriteTimeUtc < cutoff) files.Add(new(file, info.Length, info.LastWriteTimeUtc));
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        { Note(notes, "A linked or unavailable log file was kept: " + file); }
                    }
                    if (files.Count > 0) batches.Add(new("Codex logs", null, files, []));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Note(notes, error.Message); }
            }
            var temp = Path.Combine(home, "tmp");
            if (Directory.Exists(temp))
            {
                EnsurePlainPath(home, temp);
                foreach (var folder in Directory.EnumerateDirectories(temp, "mini-codex-skill-*"))
                {
                    var suffix = Path.GetFileName(folder)["mini-codex-skill-".Length..];
                    if (Guid.TryParseExact(suffix, "N", out _)) AddDirectory(folder, "Mini Codex install leftovers", false);
                }
                var helpers = Path.Combine(temp, "arg0");
                if (Directory.Exists(helpers))
                {
                    EnsurePlainPath(home, helpers);
                    foreach (var folder in Directory.EnumerateDirectories(helpers, "codex-arg0*"))
                        AddDirectory(folder, "Unused Codex helper files", true);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Note(notes, error.Message); }
        return new(home, batches, notes);

        void AddDirectory(string folder, string category, bool locked)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0) throw new IOException("The scan reached 50,000 entries. Some files were not included.");
            try
            {
                EnsurePlainPath(home, folder);
                if (Directory.GetLastWriteTimeUtc(folder) >= cutoff) return;
                var lockPath = locked ? Path.Combine(folder, ".lock") : null;
                // Codex holds this byte-range lock for the helper process's lifetime.
                using var guard = lockPath is null ? null : OpenGuard(home, lockPath);
                var files = new List<TemporaryDataFile>();
                var directories = new List<string>();
                if (ReadTree(folder, 0) && files.Count > 0) batches.Add(new(category, lockPath, files, directories));

                bool ReadTree(string directory, int depth)
                {
                    if (depth > MaxDepth) throw new IOException("A temporary folder exceeded the scan depth limit and was kept.");
                    EnsurePlainPath(home, directory);
                    directories.Add(directory);
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (--remaining < 0) throw new IOException("The scan reached 50,000 entries. Some files were not included.");
                        EnsurePlainPath(home, entry);
                        if (File.GetAttributes(entry).HasFlag(FileAttributes.Directory))
                        {
                            if (Directory.GetLastWriteTimeUtc(entry) >= cutoff || !ReadTree(entry, depth + 1)) return false;
                        }
                        else
                        {
                            var info = new FileInfo(entry);
                            if (info.LastWriteTimeUtc >= cutoff) return false;
                            files.Add(new(entry, info.Length, info.LastWriteTimeUtc));
                        }
                    }
                    return true;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Note(notes, "A temporary folder was kept because it is in use or unavailable: " + folder); }
        }
    }

    public static TemporaryDataResult Clear(TemporaryDataSnapshot snapshot, CancellationToken cancellation)
    {
        var deleted = 0; var skipped = 0; long bytes = 0;
        var notes = new List<string>();
        foreach (var batch in snapshot.Batches)
        {
            cancellation.ThrowIfCancellationRequested();
            var handledBefore = deleted + skipped;
            try
            {
                using (var guard = batch.LockPath is null ? null : OpenGuard(snapshot.Home, batch.LockPath))
                {
                    var failed = false;
                    foreach (var file in batch.Files.Where(file => !FolderPaths.Comparer.Equals(file.Path, batch.LockPath)))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        try
                        {
                            EnsurePlainPath(snapshot.Home, file.Path);
                            // Deny concurrent readers/writers while allowing our delete request.
                            using var handle = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Delete);
                            if (handle.Length != file.Bytes || File.GetLastWriteTimeUtc(file.Path) != file.Updated)
                                throw new IOException("The file changed since the scan.");
                            EnsurePlainPath(snapshot.Home, file.Path);
                            File.Delete(file.Path); deleted++; bytes += file.Bytes;
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        { skipped++; failed = true; Note(notes, "Kept an in-use, changed, or unavailable file: " + file.Path); }
                    }
                    if (batch.LockPath is { } lockPath)
                    {
                        if (failed) skipped++;
                        else
                        {
                            cancellation.ThrowIfCancellationRequested();
                            EnsurePlainPath(snapshot.Home, lockPath);
                            var file = batch.Files.Single(file => FolderPaths.Comparer.Equals(file.Path, lockPath));
                            File.Delete(lockPath); deleted++; bytes += file.Bytes;
                        }
                    }
                }
                foreach (var directory in batch.Directories.Reverse())
                {
                    cancellation.ThrowIfCancellationRequested();
                    try { EnsurePlainPath(snapshot.Home, directory); Directory.Delete(directory, recursive: false); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                skipped += batch.Files.Count - (deleted + skipped - handledBefore);
                Note(notes, "A temporary folder was kept because it is in use or unavailable.");
            }
        }
        return new(deleted, bytes, skipped, notes);
    }

    private static FileStream OpenGuard(string home, string lockPath)
    {
        EnsurePlainPath(home, lockPath);
        var guard = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        try { guard.Lock(0, long.MaxValue); return guard; }
        catch { guard.Dispose(); throw; }
    }

    public static void EnsurePlainPath(string root, string candidate)
    {
        var fullPath = Path.GetFullPath(candidate);
        if (!FolderPaths.ContainsFolder(root, fullPath)) throw new IOException("The cleanup path is outside its data folder.");
        for (string? current = fullPath; current is not null; current = Path.GetDirectoryName(current))
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Linked files and folders are kept during cleanup.");
    }

    private static void Note(List<string> notes, string message) { if (notes.Count < 10 && !notes.Contains(message)) notes.Add(message); }
    public static string Size(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GiB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.##} MiB" : bytes >= 1024 ? $"{bytes / 1024d:0.##} KiB" : $"{bytes} B";
}
