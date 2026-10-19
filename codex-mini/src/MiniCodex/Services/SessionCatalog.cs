using System.Text.Json;
using MiniCodex.Codex;
using MiniCodex.Models;

namespace MiniCodex.Services;

public sealed class SessionCatalog(AppController controller, RuntimeRegistry runtimes, Func<Guid, bool> hasWindow)
{
    private sealed record Summary(string Id, string Name, string Cwd, string? ProjectId, bool? Pinned, DateTimeOffset UpdatedAt, bool IsActive);
    private sealed class Listing
    {
        public List<Summary> Rows { get; } = [];
        public string? Cursor { get; set; }
        public bool Loaded { get; set; }
        public bool Loading { get; set; }
        public string Error { get; set; } = "";
    }
    private readonly Dictionary<Guid, IReadOnlyList<CodexProject>> _projects = [];
    private readonly Dictionary<Guid, string> _projectNotices = [];
    private readonly Dictionary<Guid, DesktopProjectAssignments> _desktop = [];
    private readonly Dictionary<Guid, RepositoryFolders> _repositories = [];
    private readonly Dictionary<Guid, string> _desktopNotices = [];
    private readonly Dictionary<(Guid Profile, bool Archived), Listing> _listings = [];
    private readonly Dictionary<(Guid Profile, bool Archived), Task> _pending = [];
    private readonly HashSet<(Guid Profile, bool Archived)> _refreshAgain = [];
    private readonly HashSet<(Guid Profile, string Thread)> _unlistedThreads = [];
    private readonly Dictionary<Guid, int> _epochs = [];
    public event EventHandler? Changed;
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public IReadOnlyList<CodexProject> Projects(Guid profileId) => _projects.GetValueOrDefault(profileId) ?? [];
    private Listing ListingFor(Guid profileId, bool archived)
    {
        var key = (profileId, archived);
        if (!_listings.TryGetValue(key, out var listing)) _listings[key] = listing = new Listing();
        return listing;
    }
    public bool IsLoading(Guid profileId, bool archived) => ListingFor(profileId, archived).Loading;
    public bool HasMore(Guid profileId, bool archived) => ListingFor(profileId, archived) is { Loading: false, Cursor: not null };
    public string Status(Guid profileId, bool archived)
    {
        var listing = ListingFor(profileId, archived);
        return listing.Loading ? $"Loading sessions · {listing.Rows.Count}" : listing.Error.Length > 0 ? "Could not finish refreshing sessions"
            : $"{listing.Rows.Count} sessions" + (listing.Cursor is not null ? " · More available" : "");
    }
    public string Details(Guid profileId, bool archived) => string.Join("\n", new[] { ListingFor(profileId, archived).Error, _projectNotices.GetValueOrDefault(profileId) ?? "",
        _desktopNotices.GetValueOrDefault(profileId) ?? "", "Codex home: " + CodexInstallation.Home(controller.Profile(profileId)),
        "Project IDs determine membership. Legacy chats use this home's saved Desktop assignments; unassigned chats use their primary repository folder." }.Where(value => value.Length > 0));

    public IReadOnlyList<SessionListItem> Rows(Guid profileId, bool archived, string search)
    {
        var profile = controller.State.Profiles.FirstOrDefault(item => item.Id == profileId);
        if (profile is null) return [];
        var listing = ListingFor(profileId, archived);
        var remoteIds = listing.Rows.Select(row => row.Id).ToHashSet();
        var result = new Dictionary<string, SessionListItem>();
        var known = new Dictionary<string, (ProjectState Project, ChatState Chat)>();
        var projects = Projects(profileId);
        var desktop = _desktop.GetValueOrDefault(profileId);
        if (!_repositories.TryGetValue(profileId, out var repositories)) _repositories[profileId] = repositories = new();
        (ProjectState? Local, CodexProject? Remote) Resolve(ProjectState? saved, string cwd, string? projectId)
        {
            if (projectId is not null)
                return (saved?.CodexProjectId == projectId ? saved : profile.Projects.FirstOrDefault(project => project.CodexProjectId == projectId),
                    projects.FirstOrDefault(project => project.Id == projectId) ?? desktop?.Projects.GetValueOrDefault(projectId));
            var canonical = repositories.Resolve(cwd);
            if (saved is not null && !(ProjectMatching.IsInferredFolder(saved) && !FolderPaths.SameFolder(repositories.Resolve(saved.PrimaryFolder), saved.PrimaryFolder))
                && (!saved.IsFolderGroup || FolderPaths.SameFolder(saved.PrimaryFolder, cwd)))
                return (saved, projects.FirstOrDefault(project => project.Id == saved.CodexProjectId));
            int Score(string primary, bool folderGroup) => folderGroup
                ? FolderPaths.SameFolder(primary, cwd) ? primary.Length : -1
                : Math.Max(ProjectMatching.FolderMatch([primary], cwd), ProjectMatching.FolderMatch([primary], canonical));
            var local = profile.Projects.Where(project => !ProjectMatching.IsInferredFolder(project) || FolderPaths.SameFolder(repositories.Resolve(project.PrimaryFolder), project.PrimaryFolder))
                .Select(project => (Project: project, Match: Score(project.PrimaryFolder, project.IsFolderGroup))).Where(match => match.Match >= 0).ToArray();
            var remote = projects.Where(project => project.Folders.Count > 0)
                .Select(project => (Project: project, Match: Score(project.Folders[0], project.Folders.All(FolderPaths.IsDefaultChatFolder)))).Where(match => match.Match >= 0).ToArray();
            var best = local.Select(match => match.Match).Concat(remote.Select(match => match.Match)).DefaultIfEmpty(-1).Max();
            var remoteOwners = remote.Where(match => match.Match == best).ToArray();
            if (remoteOwners.Length == 1)
            {
                var owner = remoteOwners[0].Project;
                return (profile.Projects.FirstOrDefault(project => project.CodexProjectId == owner.Id), owner);
            }
            if (remoteOwners.Length > 1) return (null, null);
            var localOwners = local.Where(match => match.Match == best).ToArray();
            return localOwners.Length == 1 ? (localOwners[0].Project, null) : (null, null);
        }
        SessionListItem Row(ProjectState? saved, ChatState? chat, Summary? summary)
        {
            var cwd = summary?.Cwd is { Length: > 0 } working ? working : chat?.WorkingDirectory ?? saved?.PrimaryFolder ?? "";
            var thread = summary?.Id ?? chat?.CodexThreadId;
            var nativeId = summary is not null ? summary.ProjectId : chat?.NativeProjectId;
            var legacyId = thread is not null ? desktop?.Threads.GetValueOrDefault(thread) : null;
            if (legacyId is not null && _projects.ContainsKey(profileId) && !projects.Any(project => project.Id == legacyId)) legacyId = null;
            var (owner, remote) = summary is null && chat?.CodexThreadId is null
                ? (saved, projects.FirstOrDefault(project => project.Id == saved?.CodexProjectId)) : Resolve(saved, cwd, nativeId ?? legacyId);
            IReadOnlyList<string> folders = owner is not null ? owner.Folders
                : remote?.Folders.Count > 0 ? remote.Folders : cwd.Length > 0 ? new[] { repositories.Resolve(cwd) } : Array.Empty<string>();
            var primary = owner?.PrimaryFolder ?? folders.FirstOrDefault() ?? repositories.Resolve(cwd);
            var name = remote?.Name ?? owner?.Name ?? FolderPaths.Name(cwd, "Unknown folder");
            return new SessionListItem(profileId, profile.Name, owner?.Id, owner is not null && owner.Id == saved?.Id ? chat?.Id : null,
                summary?.Id ?? chat?.CodexThreadId, chat is { HasCustomName: true } ? chat.Name : summary?.Name ?? chat!.Name, name, primary, folders,
                summary?.Pinned ?? chat?.IsPinned ?? false, archived, summary?.UpdatedAt ?? chat!.CreatedAt, true,
                remote?.Id ?? nativeId ?? owner?.CodexProjectId, cwd.Length > 0 ? cwd : null,
                owner?.IsFolderGroup ?? (remote is not null ? remote.Folders.Count > 0 && remote.Folders.All(FolderPaths.IsDefaultChatFolder) : FolderPaths.IsDefaultChatFolder(cwd)),
                summary?.IsActive ?? false, nativeId) { HasUnreadCompletion = chat?.HasUnreadCompletion ?? false };
        }
        foreach (var project in profile.Projects)
            foreach (var chat in project.Chats)
            {
                if (chat.CodexThreadId is { } thread) known.TryAdd(thread, (project, chat));
                if (chat.IsArchived != archived || chat.CodexThreadId is { } remote && listing.Loaded && !listing.Loading && listing.Cursor is null && listing.Error.Length == 0 && !remoteIds.Contains(remote) && !_unlistedThreads.Contains((profileId, remote))) continue;
                var row = Row(project, chat, null);
                result.TryAdd(row.Key, row);
            }
        foreach (var summary in listing.Rows)
        {
            known.TryGetValue(summary.Id, out var local);
            var item = Row(local.Project, local.Chat, summary);
            result[item.Key] = item;
        }
        return result.Values.Where(row => row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || row.ProjectName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || FolderPaths.Name(row.PrimaryFolder, row.ProjectName).Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.IsPinned).ThenByDescending(row => row.UpdatedAt).ThenBy(row => row.Name).ToArray();
    }
    public Task EnsureLoadedAsync(Guid profileId, bool archived)
    {
        if (_pending.TryGetValue((profileId, archived), out var pending)) return pending;
        return ListingFor(profileId, archived).Loaded ? Task.CompletedTask : RefreshAsync(profileId, archived);
    }
    public async Task<IReadOnlyList<SessionListItem>> ProjectChatsAsync(Guid profileId, Guid? projectId, bool? archivedOnly = null)
    {
        foreach (var archived in archivedOnly is { } filter ? new[] { filter } : new[] { false, true })
        {
            await RefreshAsync(profileId, archived, refreshAfterPending: true);
            var cursors = new HashSet<string>();
            while (ListingFor(profileId, archived).Cursor is { } cursor && ListingFor(profileId, archived).Error.Length == 0)
            {
                if (!cursors.Add(cursor)) throw new IOException("Codex repeated a session cursor. Refresh before changing project chats.");
                await RefreshAsync(profileId, archived, more: true);
            }
            if (ListingFor(profileId, archived).Error is { Length: > 0 } error)
                throw new IOException("Could not load every project chat. " + error);
        }
        var rows = (archivedOnly is { } selected ? Rows(profileId, selected, "") : Rows(profileId, false, "").Concat(Rows(profileId, true, ""))).ToArray();
        var profile = controller.Profile(profileId);
        if (projectId is { } ownerId)
        {
            var project = AppController.Project(controller.State, profileId, ownerId);
            var known = project.Chats.Select(chat => chat.CodexThreadId).OfType<string>().ToHashSet();
            if (rows.Any(row => row.ThreadId is { } thread && known.Contains(thread) && row.ProjectId != projectId))
                throw new InvalidOperationException("Some saved chats now belong to another project. Open those chats to update their project links before changing project chats.");
            var members = rows.Where(row => row.ProjectId == projectId).Select(row => row.ThreadId).OfType<string>().ToHashSet();
            if (profile.Projects.Where(other => other.Id != projectId).SelectMany(other => other.Chats)
                .Any(chat => chat.CodexThreadId is { } thread && members.Contains(thread)))
                throw new InvalidOperationException("Some project chats still have older folder links. Open those chats to update the links before changing project chats.");
        }
        var local = profile.Projects.Where(project => projectId is null || project.Id == projectId).SelectMany(project => project.Chats
            .Where(chat => archivedOnly is null || chat.IsArchived == archivedOnly && chat.CodexThreadId is null).Select(chat => new SessionListItem(profileId, profile.Name, project.Id, chat.Id,
            chat.CodexThreadId, chat.Name, project.Name, project.PrimaryFolder, project.Folders, chat.IsPinned, chat.IsArchived,
            chat.CreatedAt, true, project.CodexProjectId, chat.WorkingDirectory, project.IsFolderGroup)));
        return rows.Where(row => projectId is null || row.ProjectId == projectId).Concat(local).DistinctBy(row => row.Key).ToArray();
    }
    public void ForgetProject(Guid profileId, string? remoteId)
    {
        _epochs[profileId] = _epochs.GetValueOrDefault(profileId) + 1;
        if (remoteId is not null && _projects.TryGetValue(profileId, out var projects))
            _projects[profileId] = projects.Where(project => project.Id != remoteId).ToArray();
        Notify();
    }
    public Task RefreshForChangeAsync(Guid profileId, string? createdThread)
    {
        if (createdThread is not null) _unlistedThreads.Add((profileId, createdThread));
        return RefreshAsync(profileId, false, refreshAfterPending: true);
    }
    public Task RefreshAsync(Guid profileId, bool archived, bool more = false, bool refreshAfterPending = false)
    {
        var key = (profileId, archived);
        if (_pending.TryGetValue(key, out var pending)) { if (refreshAfterPending) _refreshAgain.Add(key); return pending; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[key] = completion.Task;
        _ = RunAsync();
        return completion.Task;
        async Task RunAsync()
        {
            try
            {
                await RefreshCoreAsync(profileId, archived, more);
                while (_refreshAgain.Remove(key)) await RefreshCoreAsync(profileId, archived, false);
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { if (ReferenceEquals(_pending.GetValueOrDefault(key), completion.Task)) _pending.Remove(key); }
        }
    }
    private async Task LoadProjectsAsync(Guid profileId, AccountRuntime runtime, int epoch)
    {
        try
        {
            var projects = new List<CodexProject>();
            string? cursor = null;
            var cursors = new HashSet<string>();
            for (var page = 0; page < 50; page++)
            {
                var result = await runtime.ListProjectsAsync(cursor);
                foreach (var project in result.Get("data").Items())
                {
                    if (project.Text("id") is not { } id) continue;
                    var folders = project.Get("roots").Items().Select(root => root.Text("path")).OfType<string>()
                        .Where(Path.IsPathFullyQualified).Select(Path.TrimEndingDirectorySeparator).Distinct(FolderPaths.Comparer).ToArray();
                    projects.Add(new(id, project.Text("name") ?? "Codex project", folders, project.Get("position").Number() ?? projects.Count));
                }
                cursor = result.Text("nextCursor");
                if (cursor is null) break;
                if (!cursors.Add(cursor)) throw new IOException("Codex repeated a project cursor.");
                if (page == 49) throw new IOException("Project list exceeds 5,000 entries.");
            }
            if (_epochs.GetValueOrDefault(profileId) != epoch || runtimes.IsRemoving(profileId)) return;
            _projects[profileId] = projects.DistinctBy(project => project.Id).OrderBy(project => project.Position).ToArray();
            controller.LinkCodexProjects(profileId, _projects[profileId]);
            _projectNotices[profileId] = projects.Count == 0
                ? "Codex returned no project records. Legacy Desktop sidebar folders may be stored only in Desktop; these sessions are grouped by working folder." : "";
        }
        catch (Exception error) when (error is IOException or TimeoutException)
        {
            if (_epochs.GetValueOrDefault(profileId) == epoch && !runtimes.IsRemoving(profileId))
                _projectNotices[profileId] = "Project metadata unavailable: " + Wire.Short(error.Message, 400) + "\nGrouping available sessions by their folders.";
        }
    }
    private async Task RefreshCoreAsync(Guid profileId, bool archived, bool more)
    {
        var profile = controller.State.Profiles.FirstOrDefault(item => item.Id == profileId);
        if (profile is null || runtimes.IsStopping || runtimes.IsRemoving(profileId)) return;
        var listing = ListingFor(profileId, archived);
        var epoch = _epochs.GetValueOrDefault(profileId);
        if (more && listing.Loaded && listing.Cursor is null) return;
        listing.Loading = true; listing.Error = ""; Notify();
        RuntimeRegistry.RuntimeLease? lease = null;
        try
        {
            if (!Directory.Exists(CodexInstallation.Home(profile))) { listing.Error = "Codex home is unavailable."; return; }
            if (!more)
            {
                try
                {
                    var desktop = await Task.Run(() => DesktopProjectAssignments.Read(CodexInstallation.Home(profile)));
                    if (_epochs.GetValueOrDefault(profileId) != epoch || runtimes.IsRemoving(profileId)) return;
                    _desktop[profileId] = desktop; _desktopNotices.Remove(profileId); _repositories[profileId] = new();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { _desktopNotices[profileId] = "Legacy Desktop assignments could not be refreshed: " + Wire.Short(error.Message, 250); }
            }
            lease = await runtimes.BorrowAsync(profileId);
            if (!lease.Runtime.Connected) await lease.Runtime.ConnectAsync();
            if (!more) await LoadProjectsAsync(profileId, lease.Runtime, epoch);
            if (_epochs.GetValueOrDefault(profileId) != epoch || runtimes.IsRemoving(profileId)) return;
            var cursor = more ? listing.Cursor : null;
            var cursors = new HashSet<string>();
            for (var pageIndex = 0; pageIndex < 50; pageIndex++)
            {
                var page = await lease.Runtime.ListSessionsAsync(archived, cursor);
                if (_epochs.GetValueOrDefault(profileId) != epoch || runtimes.IsRemoving(profileId) || !controller.State.Profiles.Any(item => item.Id == profileId)) return;
                if (!more && pageIndex == 0) listing.Rows.Clear();
                var incoming = page.Get("data").Items().Select(ReadSummary).OfType<Summary>().ToArray();
                foreach (var row in incoming) _unlistedThreads.Remove((profileId, row.Id));
                foreach (var row in incoming) { listing.Rows.RemoveAll(item => item.Id == row.Id); listing.Rows.Add(row); }
                listing.Cursor = page.Text("nextCursor"); listing.Loaded = true;
                UpdateKnownChats(profileId, archived, incoming);
                Notify();
                if (listing.Cursor is null) break;
                if (!cursors.Add(listing.Cursor) || listing.Cursor == cursor) throw new IOException("Codex repeated a session cursor. Refresh the list before loading more.");
                cursor = listing.Cursor;
            }
        }
        catch (Exception error) { listing.Error = Wire.Short(error.Message, 500); }
        finally
        {
            if (lease is not null)
            {
                try { try { if (!hasWindow(profileId)) await runtimes.ReleaseAsync(profileId); } finally { await lease.DisposeAsync(); } }
                catch (Exception error) { listing.Error = Wire.Short(error.Message, 500); }
            }
            listing.Loading = false; Notify();
        }
    }
    private void UpdateKnownChats(Guid profileId, bool archived, IReadOnlyList<Summary> incoming)
    {
        var updates = incoming.ToDictionary(summary => summary.Id);
        var chats = controller.Profile(profileId).Projects.SelectMany(project => project.Chats).Where(chat => chat.CodexThreadId is not null && updates.ContainsKey(chat.CodexThreadId)).ToArray();
        if (!chats.Any(chat => updates[chat.CodexThreadId!] is { } summary && (!chat.HasCustomName && chat.Name != summary.Name || summary.Pinned is { } pin && chat.IsPinned != pin || chat.IsArchived != archived || summary.Cwd.Length > 0 && chat.WorkingDirectory != summary.Cwd
            || chat.NativeProjectId != summary.ProjectId))) return;
        controller.Commit(state =>
        {
            foreach (var chat in AppController.Profile(state, profileId).Projects.SelectMany(project => project.Chats))
                if (chat.CodexThreadId is { } id && updates.TryGetValue(id, out var summary))
                { if (!chat.HasCustomName) chat.Name = summary.Name; if (summary.Pinned is { } pin) chat.IsPinned = pin; chat.IsArchived = archived; if (summary.Cwd.Length > 0) chat.WorkingDirectory = summary.Cwd; chat.NativeProjectId = summary.ProjectId; }
        });
    }
    private static Summary? ReadSummary(JsonElement item)
    {
        if (item.Text("id") is not { } id) return null;
        var name = Wire.Short(item.Text("name") ?? item.Text("preview") ?? "Codex chat", 160);
        var timestamp = item.Get("recencyAt").Number() ?? item.Get("updatedAt").Number() ?? item.Get("createdAt").Number() ?? 0;
        var updated = timestamp >= 0 && timestamp <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(timestamp) : DateTimeOffset.UnixEpoch;
        bool? pinned = item.Get("isPinned").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
        return new(id, string.IsNullOrWhiteSpace(name) ? "Codex chat" : name, item.Text("cwd") ?? "", item.Text("projectId"), pinned, updated, item.Get("status").Text("type") == "active");
    }
    public void MarkArchived(SessionListItem row)
    {
        if (row.ThreadId is not { } threadId) { Notify(); return; }
        var summary = ListingFor(row.ProfileId, false).Rows.FirstOrDefault(item => item.Id == threadId)
            ?? new Summary(threadId, row.Name, row.WorkingDirectory ?? row.PrimaryFolder, row.NativeProjectId, row.IsPinned, row.UpdatedAt, false);
        _unlistedThreads.Remove((row.ProfileId, threadId));
        foreach (var listing in _listings.Where(pair => pair.Key.Profile == row.ProfileId).Select(pair => pair.Value))
            listing.Rows.RemoveAll(item => item.Id == threadId);
        ListingFor(row.ProfileId, true).Rows.Add(summary with { IsActive = false });
        Notify();
    }
    public void Invalidate(Guid profileId, string? threadId, bool reload = true)
    {
        if (threadId is null) return;
        _unlistedThreads.Remove((profileId, threadId));
        foreach (var listing in _listings.Where(pair => pair.Key.Profile == profileId).Select(pair => pair.Value))
        { listing.Rows.RemoveAll(row => row.Id == threadId); if (reload) listing.Loaded = false; }
        Notify();
    }
    public void ForgetProfile(Guid profileId)
    {
        _epochs[profileId] = _epochs.GetValueOrDefault(profileId) + 1;
        foreach (var key in _listings.Keys.Where(key => key.Profile == profileId).ToArray()) _listings.Remove(key);
        foreach (var key in _pending.Keys.Where(key => key.Profile == profileId).ToArray()) _pending.Remove(key);
        _refreshAgain.RemoveWhere(key => key.Profile == profileId);
        _unlistedThreads.RemoveWhere(key => key.Profile == profileId);
        _projects.Remove(profileId); _projectNotices.Remove(profileId);
        _desktop.Remove(profileId); _repositories.Remove(profileId); _desktopNotices.Remove(profileId);
        Notify();
    }
}
