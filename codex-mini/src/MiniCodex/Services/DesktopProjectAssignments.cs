using System.Text.Json;
using MiniCodex.Codex;
using MiniCodex.Models;

namespace MiniCodex.Services;

internal sealed class DesktopProjectAssignments
{
    public Dictionary<string, string> Threads { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, CodexProject> Projects { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Projectless { get; } = new(StringComparer.Ordinal);

    public static DesktopProjectAssignments Read(string home)
    {
        var result = new DesktopProjectAssignments();
        var path = Path.Combine(home, ".codex-global-state.json");
        if (!File.Exists(path)) return result;
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new IOException("Desktop project metadata exceeds 32 MiB.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var mappings = root.Get("app-server-project-id-by-legacy-project-id-by-host");
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mappings.ValueKind == JsonValueKind.Object)
            foreach (var host in mappings.EnumerateObject())
            {
                if (!host.Name.StartsWith("local:", StringComparison.OrdinalIgnoreCase) || !FolderPaths.SameFolder(host.Name[6..], home) || host.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var entry in host.Value.EnumerateObject()) if (entry.Value.Text() is { Length: > 0 } mappedId) ids[entry.Name] = mappedId;
            }
        var projects = root.Get("local-projects");
        var rows = projects.ValueKind == JsonValueKind.Object ? projects.EnumerateObject().Select(property => property.Value) : projects.Items();
        foreach (var project in rows)
        {
            if (project.Text("id") is not { } legacyId || !ids.TryGetValue(legacyId, out var nativeId)) continue;
            var folders = project.Get("rootPaths").Items().Select(value => value.Text()).OfType<string>()
                .Where(Path.IsPathFullyQualified).Select(FolderPaths.Normalize).Distinct(FolderPaths.Comparer).ToArray();
            if (folders.Length == 0) continue;
            result.Projects[nativeId] = new(nativeId, project.Text("name") ?? FolderPaths.Name(folders[0], "Project"), folders, result.Projects.Count);
        }
        foreach (var value in root.Get("projectless-thread-ids").Items()) if (value.Text() is { } projectlessId) result.Projectless.Add(projectlessId);
        var assignments = root.Get("thread-project-assignments");
        if (assignments.ValueKind == JsonValueKind.Object)
            foreach (var entry in assignments.EnumerateObject())
                if (!result.Projectless.Contains(entry.Name) && entry.Value.Text("projectKind") == "local"
                    && entry.Value.Text("projectId") is { } assignedLegacy && ids.TryGetValue(assignedLegacy, out var assignedNative)) result.Threads[entry.Name] = assignedNative;
        return result;
    }
}
