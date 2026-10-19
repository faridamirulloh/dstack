using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MiniCodex.Codex;
using MiniCodex.Models;

namespace MiniCodex.Services;

public static class GitHubSkills
{
    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MiniCodex/0.2");
        return client;
    }
    private static string Segment(string text) => Uri.EscapeDataString(text);
    private static string RepoPath(SkillSource source) => Segment(source.Owner) + "/" + Segment(source.Repository);
    private static string UrlPath(string path) => string.Join('/', path.Split('/').Select(Segment));
    private static async Task<JsonElement> JsonAsync(string url, CancellationToken cancellation)
    {
        var bytes = await DownloadAsync(url, 4 * 1024 * 1024, cancellation);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }
    private static async Task<byte[]> DownloadAsync(string url, int limit, CancellationToken cancellation)
    {
        using var output = new MemoryStream();
        await DownloadToAsync(url, output, limit, cancellation);
        return output.ToArray();
    }
    private static async Task DownloadToAsync(string url, Stream output, long limit, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
            if (!response.IsSuccessStatusCode) throw new IOException($"GitHub returned HTTP {(int)response.StatusCode}. Use a public repository and check the URL; API rate limits may require waiting before Refresh.");
            if (response.Content.Headers.ContentLength > limit) throw new IOException("The GitHub download exceeds the supported size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(lifetime.Token);
            var buffer = new byte[81920]; long size = 0; int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), lifetime.Token)) > 0)
            {
                size += read;
                if (size > limit) throw new IOException("The GitHub download exceeds the supported size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new IOException("The GitHub request timed out. Choose Refresh to retry."); }
    }
    private static async Task<string> ResolveCommitAsync(string owner, string repository, string reference, CancellationToken cancellation)
    {
        var result = await JsonAsync($"https://api.github.com/repos/{Segment(owner)}/{Segment(repository)}/commits/{Segment(reference)}", cancellation);
        var sha = result.Text("sha");
        if (sha is null || !Regex.IsMatch(sha, "^[a-fA-F0-9]{40}$")) throw new IOException("GitHub did not return a usable commit for this skill.");
        return sha;
    }
    public static async Task<IReadOnlyList<ExtensionPackage>> ListCuratedAsync(string home, CancellationToken cancellation)
    {
        var commit = await ResolveCommitAsync("openai", "skills", "main", cancellation);
        var data = await JsonAsync($"https://api.github.com/repos/openai/skills/contents/skills/.curated?ref={commit}", cancellation);
        var folders = data.Items().Where(row => row.Text("type") == "dir").Select(row => row.Text("path")).OfType<string>().ToArray();
        if (folders.Length > 200) throw new IOException("The curated catalog exceeds 200 skills. Install a specific public GitHub skill using Preview skill.");
        var packages = new ExtensionPackage[folders.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, folders.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellation }, async (index, token) =>
        {
            var source = new SkillSource("openai", "skills", commit, folders[index]);
            try { packages[index] = await ReadSkillAsync(source, home, token); }
            catch (Exception error) when (error is IOException or HttpRequestException or JsonException or ArgumentException)
            {
                packages[index] = new ExtensionPackage
                {
                    Key = "skill/" + source.Url, Name = source.DirectoryName, Kind = ReferenceKind.Skill, Source = "openai/skills · curated", Skill = source,
                    Available = false, Description = "Could not read skill metadata. " + Wire.Short(error.Message, 400), Compatibility = "Refresh to retry metadata discovery."
                };
            }
        });
        return packages.OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static async Task<ExtensionPackage> PreviewAsync(string url, string home, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || uri.UserInfo.Length > 0 || !uri.IsDefaultPort || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Enter a public https://github.com/owner/repo/tree/ref/skill-folder URL.");
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length < 2) throw new ArgumentException("The URL must identify a GitHub repository and skill folder.");
        var owner = parts[0]; var repository = parts[1];
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repository = repository[..^4];
        if (!Regex.IsMatch(owner, "^[a-zA-Z0-9_.-]+$") || !Regex.IsMatch(repository, "^[a-zA-Z0-9_.-]+$")) throw new ArgumentException("Invalid GitHub repository name.");
        var reference = "HEAD"; var folder = "";
        if (parts.Length > 2)
        {
            if (parts.Length < 4 || parts[2] is not ("tree" or "blob")) throw new ArgumentException("Use a GitHub skill folder or SKILL.md URL with a tree/blob reference.");
            reference = parts[3]; folder = string.Join('/', parts.Skip(4));
            if (folder == "SKILL.md") folder = "";
            else if (folder.EndsWith("/SKILL.md", StringComparison.Ordinal)) folder = folder[..^9];
        }
        var source = new SkillSource(owner, repository, await ResolveCommitAsync(owner, repository, reference, cancellation), folder);
        return await ReadSkillAsync(source, home, cancellation);
    }
    private static async Task<ExtensionPackage> ReadSkillAsync(SkillSource source, string home, CancellationToken cancellation)
    {
        ValidateSource(source);
        var path = (source.Folder.Length > 0 ? source.Folder + "/" : "") + "SKILL.md";
        var text = Encoding.UTF8.GetString(await DownloadAsync($"https://raw.githubusercontent.com/{RepoPath(source)}/{source.Commit}/{UrlPath(path)}", 128 * 1024, cancellation));
        var (name, description) = Metadata(text);
        var destination = Path.Combine(home, "skills", source.DirectoryName);
        var occupied = Path.Exists(destination);
        var installed = File.Exists(Path.Combine(destination, "SKILL.md"));
        return new ExtensionPackage
        {
            Key = "skill/" + source.Url, Name = name, Kind = ReferenceKind.Skill, Skill = source,
            Source = source.Owner + "/" + source.Repository, Version = source.Commit[..7], Description = description,
            Installed = installed, Available = !occupied || installed,
            Compatibility = "Standalone Codex skill pinned to " + source.Commit[..7] + ". Check its instructions for platform and tool requirements. Bundled scripts are not run during installation."
                + (occupied && !installed ? " An existing path blocks installation: " + destination : "")
        };
    }
    private static (string Name, string Description) Metadata(string text)
    {
        var lines = text.TrimStart('\uFEFF').ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---") throw new IOException("SKILL.md must start with YAML metadata containing name and description.");
        var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (end < 0) throw new IOException("SKILL.md has incomplete YAML metadata.");
        string Field(string key)
        {
            var index = Array.FindIndex(lines, 1, end - 1, line => line.StartsWith(key + ":", StringComparison.Ordinal));
            if (index < 0) return "";
            var value = lines[index][(key.Length + 1)..].Trim();
            if (value.Length == 0 || value[0] is '|' or '>')
            {
                var continuation = new List<string>();
                for (var next = index + 1; next < end && (lines[next].Length == 0 || char.IsWhiteSpace(lines[next][0])); next++) continuation.Add(lines[next].Trim());
                value = string.Join(value.StartsWith('|') ? "\n" : " ", continuation).Trim();
            }
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') value = value[1..^1].Replace("''", "'");
            else if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            { try { value = JsonSerializer.Deserialize<string>(value) ?? ""; } catch (JsonException) { value = value[1..^1]; } }
            return value;
        }
        var name = Field("name"); var description = Field("description");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description)) throw new IOException("SKILL.md must provide a readable name and description.");
        return (Wire.Short(name, 160), Wire.Short(description, 4000));
    }
    private static void ValidatePart(string part)
    {
        if (string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.EndsWith('.') || part.EndsWith(' ')
            || Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase))
            throw new IOException("The skill contains a path that Windows cannot install safely.");
    }
    private static void ValidateSource(SkillSource source)
    {
        if (!Regex.IsMatch(source.Owner, "^[a-zA-Z0-9_.-]+$") || !Regex.IsMatch(source.Repository, "^[a-zA-Z0-9_.-]+$") || !Regex.IsMatch(source.Commit, "^[a-fA-F0-9]{40}$"))
            throw new IOException("Invalid GitHub skill source.");
        if (source.Folder.Length > 0) foreach (var part in source.Folder.Split('/')) ValidatePart(part);
        ValidatePart(source.DirectoryName);
    }
    private static void PlainDirectory(string home, string path)
    {
        var relative = Path.GetRelativePath(home, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("The install destination is outside the selected Codex home.");
        var current = home;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("The install destination contains a file, symbolic link, or junction. Choose a plain skills directory in this Codex home.");
        }
    }
    public static async Task InstallAsync(SkillSource source, string homePath, CancellationToken cancellation)
    {
        ValidateSource(source);
        var home = FolderPaths.Canonicalize(homePath);
        var skills = Path.Combine(home, "skills"); var destination = Path.Combine(skills, source.DirectoryName);
        PlainDirectory(home, skills);
        if (Path.Exists(destination)) throw new IOException("A skill folder already exists at " + destination + ". It has not been overwritten.");
        var staging = Path.Combine(home, "tmp", "mini-codex-skill-" + Guid.NewGuid().ToString("N"));
        PlainDirectory(home, staging); Directory.CreateDirectory(staging);
        try
        {
            var zipPath = Path.Combine(staging, "download.zip");
            await using (var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await DownloadToAsync($"https://codeload.github.com/{RepoPath(source)}/zip/{source.Commit}", output, 100 * 1024 * 1024, cancellation);
            var payload = Path.Combine(staging, "payload"); Directory.CreateDirectory(payload);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var roots = archive.Entries.Where(entry => entry.FullName.Length > 0).Select(entry => entry.FullName.Split('/')[0]).Distinct().ToArray();
                if (roots.Length != 1) throw new IOException("The GitHub archive has an unexpected root layout.");
                var prefix = roots[0] + "/" + (source.Folder.Length > 0 ? source.Folder + "/" : "");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0; var count = 0;
                foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var relative = entry.FullName[prefix.Length..];
                    if (relative.Length == 0) continue;
                    if (entry.FullName.Contains('\\') || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new IOException("Skill archives cannot contain symbolic links or backslash paths.");
                    var directory = relative.EndsWith('/'); var parts = relative.TrimEnd('/').Split('/');
                    foreach (var part in parts) ValidatePart(part);
                    var target = Path.Combine(payload, Path.Combine(parts));
                    if (directory) { Directory.CreateDirectory(target); continue; }
                    if (!paths.Add(target) || ++count > 5000 || entry.Length > 32 * 1024 * 1024 || (expanded += entry.Length) > 250 * 1024 * 1024)
                        throw new IOException("The skill archive has duplicate paths or exceeds the supported extraction limits.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = entry.Open();
                    await using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    var buffer = new byte[81920]; long written = 0; int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(), cancellation)) > 0)
                    {
                        written += read;
                        if (written > entry.Length) throw new IOException("The skill archive contains an invalid file size.");
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    }
                    if (written != entry.Length) throw new IOException("The skill archive contains an incomplete file.");
                }
            }
            var manifest = Path.Combine(payload, "SKILL.md");
            if (!File.Exists(manifest)) throw new IOException("The selected folder does not contain SKILL.md.");
            _ = Metadata(await File.ReadAllTextAsync(manifest, cancellation));
            cancellation.ThrowIfCancellationRequested(); PlainDirectory(home, skills);
            Directory.CreateDirectory(skills);
            if (Path.Exists(destination)) throw new IOException("The destination was installed by another process. Existing files have not been overwritten.");
            Directory.Move(payload, destination);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
