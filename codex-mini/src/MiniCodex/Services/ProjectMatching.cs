using MiniCodex.Models;
using MiniCodex.Codex;
using System.Text.Json;

namespace MiniCodex.Services;

public static class ProjectMatching
{
    public static CodexProject? RemoteFor(ProjectState local, IReadOnlyList<CodexProject> projects)
    {
        return local.CodexProjectId is { } id ? projects.FirstOrDefault(project => project.Id == id) : null;
    }

    public static int FolderMatch(IReadOnlyList<string> folders, string workingDirectory) => folders
        .Where(folder => FolderPaths.ContainsFolder(folder, workingDirectory))
        .Select(folder => Path.TrimEndingDirectorySeparator(folder).Length).DefaultIfEmpty(-1).Max();

    public static void RepairSavedProjects(AppState state)
    {
        // Run before runtimes and drafts are opened, so no async task holds a removed project ID.
        foreach (var profile in state.Profiles)
        {
            DesktopProjectAssignments desktop;
            try { desktop = DesktopProjectAssignments.Read(CodexInstallation.Home(profile)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { desktop = new(); }
            var repositories = new RepositoryFolders();
            var removed = new Dictionary<Guid, ProjectState>();
            ProjectState? ProjectForId(string id)
            {
                if (profile.Projects.FirstOrDefault(project => project.CodexProjectId == id) is { } existing) return existing;
                if (!desktop.Projects.TryGetValue(id, out var definition)) return null;
                var created = new ProjectState { Name = definition.Name, CodexProjectId = id, PrimaryFolder = definition.Folders[0], Folders = definition.Folders.ToList() };
                profile.Projects.Add(created);
                return created;
            }
            foreach (var source in profile.Projects.ToArray())
            {
                var canonical = repositories.Resolve(source.PrimaryFolder);
                var inferred = IsInferredFolder(source);
                var isWorktree = inferred && !FolderPaths.SameFolder(canonical, source.PrimaryFolder);
                ProjectState? repositoryProject = null;
                if (isWorktree)
                {
                    var definitions = desktop.Projects.Values.Where(project => FolderPaths.SameFolder(project.Folders[0], canonical)).ToArray();
                    if (definitions.Length == 1) repositoryProject = ProjectForId(definitions[0].Id);
                    else if (definitions.Length == 0)
                    {
                        var owners = profile.Projects.Where(project => project != source && FolderPaths.SameFolder(project.PrimaryFolder, canonical)).ToArray();
                        if (owners.Length == 1) repositoryProject = owners[0];
                    }
                }
                ProjectState? lastOwner = null;
                foreach (var chat in source.Chats.ToArray())
                {
                    var assigned = chat.NativeProjectId ?? (chat.CodexThreadId is { } thread ? desktop.Threads.GetValueOrDefault(thread) : null);
                    var destination = assigned is not null ? ProjectForId(assigned) : repositoryProject;
                    if (destination is null || destination == source) continue;
                    chat.WorkingDirectory ??= source.PrimaryFolder;
                    source.Chats.Remove(chat); destination.Chats.Add(chat); lastOwner = destination;
                    if (source.LastActiveAt > destination.LastActiveAt) destination.LastActiveAt = source.LastActiveAt;
                }
                // Remove only emptied inferred folder entries, never independent Codex projects.
                if (inferred && source.Chats.Count == 0 && (lastOwner ?? repositoryProject) is { } owner)
                { profile.Projects.Remove(source); removed[source.Id] = owner; }
            }
            var groups = profile.Projects.Where(project => project.CodexProjectId is not null).GroupBy(project => project.CodexProjectId!).ToArray();
            foreach (var group in groups)
            {
                var canonical = group.First();
                foreach (var alias in group.Skip(1))
                {
                    foreach (var chat in alias.Chats)
                    {
                        chat.WorkingDirectory ??= alias.PrimaryFolder;
                        canonical.Chats.Add(chat);
                    }
                    foreach (var folder in alias.Folders)
                        if (!canonical.Folders.Any(root => FolderPaths.SameFolder(root, folder))
                            && !canonical.ExcludedCodexFolders.Any(root => FolderPaths.SameFolder(root, folder))) canonical.Folders.Add(folder);
                    if (alias.LastActiveAt > canonical.LastActiveAt) canonical.LastActiveAt = alias.LastActiveAt;
                    profile.Projects.Remove(alias);
                    removed[alias.Id] = canonical;
                }
            }
            foreach (var (id, destination) in removed)
            {
                var oldKey = profile.Id.ToString("N") + "/project/" + id.ToString("N");
                var newKey = profile.Id.ToString("N") + "/project/" + (destination.CodexProjectId is { } remote ? "codex/" + remote : destination.Id.ToString("N"));
                profile.CustomProjectOrder = profile.CustomProjectOrder.Select(key => key.Equals(oldKey, StringComparison.OrdinalIgnoreCase) ? newKey : key)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    internal static bool IsInferredFolder(ProjectState project) => project.CodexProjectId is null && (project.IsImportedFolder
        ?? (project.Folders.Count == 1 && project.Chats.Count > 0 && project.Chats.All(chat => chat.CodexThreadId is not null)
            && project.Name.Equals(FolderPaths.Name(project.PrimaryFolder, ""), StringComparison.OrdinalIgnoreCase)));
}
