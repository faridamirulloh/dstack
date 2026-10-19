using MiniCodex.Models;
using MiniCodex.Codex;

namespace MiniCodex.Services;

public sealed class AppController(StateStore store)
{
    public AppState State { get; private set; } = store.Load();
    private readonly HashSet<(Guid Profile, Guid Project)> _deletingProjects = [];
    private readonly HashSet<(Guid Profile, Guid Project)> _archivingProjects = [];
    private readonly HashSet<Guid> _deletingArchivedChats = [];
    public event EventHandler? Changed;

    public bool IsDeletingProject(Guid profileId, Guid projectId) => IsDeletingArchivedChats(profileId) || _deletingProjects.Contains((profileId, projectId));
    public bool IsDeletingArchivedChats(Guid profileId) => _deletingArchivedChats.Contains(profileId);
    public bool IsArchivingProject(Guid profileId, Guid projectId) => _archivingProjects.Contains((profileId, projectId));
    public void EnsureProjectAvailable(Guid profileId, Guid projectId)
    {
        if (IsDeletingArchivedChats(profileId)) throw new InvalidOperationException("Archived chats in this account are being deleted. Wait for deletion to finish.");
        if (IsDeletingProject(profileId, projectId)) throw new InvalidOperationException("Deletion is in progress for this project. Wait for it to finish.");
        if (IsArchivingProject(profileId, projectId)) throw new InvalidOperationException("Chats in this project are being archived. Wait for archiving to finish.");
    }
    public void BeginProjectDeletion(Guid profileId, Guid projectId)
    {
        _ = Project(State, profileId, projectId);
        EnsureProjectAvailable(profileId, projectId);
        _deletingProjects.Add((profileId, projectId)); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void EndProjectDeletion(Guid profileId, Guid projectId)
    {
        if (_deletingProjects.Remove((profileId, projectId))) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void BeginProjectArchiving(Guid profileId, Guid projectId)
    {
        _ = Project(State, profileId, projectId);
        EnsureProjectAvailable(profileId, projectId);
        _archivingProjects.Add((profileId, projectId)); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void EndProjectArchiving(Guid profileId, Guid projectId)
    {
        if (_archivingProjects.Remove((profileId, projectId))) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void BeginArchivedChatsDeletion(Guid profileId)
    {
        _ = Profile(profileId);
        if (IsDeletingArchivedChats(profileId) || _deletingProjects.Any(key => key.Profile == profileId) || _archivingProjects.Any(key => key.Profile == profileId))
            throw new InvalidOperationException("Wait for this account's project changes to finish before deleting archived chats.");
        _deletingArchivedChats.Add(profileId); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void EndArchivedChatsDeletion(Guid profileId)
    {
        if (_deletingArchivedChats.Remove(profileId)) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Commit(Action<AppState> change)
    {
        var next = store.Copy(State);
        foreach (var project in next.Profiles.SelectMany(profile => profile.Projects))
        { project.IsUnassigned = project.IsFolderGroup; project.IsImportedFolder ??= ProjectMatching.IsInferredFolder(project); }
        change(next);
        store.Save(next);
        State = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ProfileState Profile(Guid id) => State.Profiles.Single(profile => profile.Id == id);

    public void SetChatUnreadCompletion(Guid profileId, Guid chatId, bool unread)
    {
        var owner = State.Profiles.FirstOrDefault(profile => profile.Id == profileId)?.Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == chatId));
        var existing = owner?.Chats.FirstOrDefault(chat => chat.Id == chatId);
        if (owner is null || existing is null || existing.HasUnreadCompletion == unread) return;
        Commit(state => Project(state, profileId, owner.Id).Chats.Single(chat => chat.Id == chatId).HasUnreadCompletion = unread);
    }

    public void MarkProjectTaskStarted(Guid profileId, Guid projectId)
    {
        EnsureProjectAvailable(profileId, projectId);
        Commit(state => Project(state, profileId, projectId).LastActiveAt = DateTimeOffset.UtcNow);
    }

    public static ProfileState Profile(AppState state, Guid id) => state.Profiles.Single(profile => profile.Id == id);

    public static ProjectState Project(AppState state, Guid profileId, Guid projectId) => Profile(state, profileId).Projects.Single(project => project.Id == projectId);

    public ProjectWorkspace WorkspaceForChat(Guid profileId, Guid projectId, Guid chatId)
    {
        EnsureProjectAvailable(profileId, projectId);
        var project = Project(State, profileId, projectId);
        var chat = project.Chats.FirstOrDefault(chat => chat.Id == chatId) ?? throw new InvalidOperationException("Chat does not belong to this project.");
        var working = chat.WorkingDirectory ?? project.PrimaryFolder;
        return new ProjectWorkspace(working, Array.AsReadOnly(project.Folders.Append(working).Distinct(FolderPaths.Comparer).ToArray()));
    }

    public Guid AddProfile(string name, string? existingCodexHome = null)
    {
        var canonicalHome = existingCodexHome is null ? null : FolderPaths.Canonicalize(existingCodexHome);
        if (canonicalHome is not null)
            foreach (var registered in State.Profiles)
            {
                var home = CodexInstallation.Home(registered);
                if (Directory.Exists(home) && FolderPaths.Comparer.Equals(canonicalHome, FolderPaths.Canonicalize(home))) return registered.Id;
            }
        var profile = new ProfileState { Name = name.Trim(), ExistingCodexHome = canonicalHome };
        Commit(state => state.Profiles.Add(profile));
        return profile.Id;
    }

    public Guid SaveProject(Guid profileId, Guid? projectId, FolderPlan plan)
    {
        if (projectId is { } existing) EnsureProjectAvailable(profileId, existing);
        var folders = plan.Folders.Select(FolderPaths.Canonicalize).Distinct(FolderPaths.Comparer).ToList();
        var primary = FolderPaths.Canonicalize(plan.PrimaryFolder);
        if (!folders.Contains(primary, FolderPaths.Comparer)) throw new InvalidOperationException("Select a primary folder from this project's folder list.");
        var id = projectId ?? Guid.NewGuid();
        Commit(state =>
        {
            var profile = Profile(state, profileId);
            var project = projectId.HasValue ? profile.Projects.Single(item => item.Id == id) : new ProjectState { Id = id };
            project.Name = plan.Name.Trim();
            project.IsUnassigned = false;
            project.IsImportedFolder = false;
            project.ExcludedCodexFolders = project.ExcludedCodexFolders.Concat(project.Folders.Where(folder => !folders.Any(root => FolderPaths.SameFolder(root, folder))))
                .Where(folder => !folders.Any(root => FolderPaths.SameFolder(root, folder))).Distinct(FolderPaths.Comparer).ToList();
            project.Folders = folders;
            project.PrimaryFolder = primary;
            if (!projectId.HasValue) profile.Projects.Add(project);
        });
        return id;
    }

    public Guid FolderGroup(Guid profileId, string folder)
    {
        var canonical = FolderPaths.Canonicalize(folder);
        var otherChats = FolderPaths.IsDefaultChatFolder(canonical);
        if (Profile(profileId).Projects.FirstOrDefault(project => ProjectMatching.IsInferredFolder(project) && project.IsFolderGroup == otherChats && FolderPaths.SameFolder(project.PrimaryFolder, canonical)) is { } existing)
        { EnsureProjectAvailable(profileId, existing.Id); return existing.Id; }
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(canonical));
        var group = new ProjectState { Name = name.Length > 0 ? name : canonical, PrimaryFolder = canonical, Folders = [canonical], IsUnassigned = otherChats, IsImportedFolder = true };
        Commit(state => Profile(state, profileId).Projects.Add(group));
        return group.Id;
    }

    public void DeleteProject(Guid profileId, Guid projectId) => Commit(state =>
    {
        var profile = Profile(state, profileId);
        var project = Project(state, profileId, projectId);
        if (project.Chats.Count > 0) throw new InvalidOperationException("Delete this project's chats before deleting the project.");
        profile.Projects.Remove(project);
        profile.CustomProjectOrder.RemoveAll(key => key.Contains(project.Id.ToString("N"), StringComparison.OrdinalIgnoreCase)
            || project.CodexProjectId is { } remote && key.EndsWith("/codex/" + remote, StringComparison.OrdinalIgnoreCase));
    });

    public void RemoveProfile(Guid profileId) => Commit(state =>
    {
        if (state.Profiles.RemoveAll(profile => profile.Id == profileId) == 0)
            throw new InvalidOperationException("This account has already been removed.");
        foreach (var layout in state.AccountWindowLayouts) layout.RemoveAll(id => id == profileId);
        state.AccountWindowLayouts.RemoveAll(layout => layout.Count == 0);
    });

    public void EditProfile(Guid profileId, string name, string home)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter an account name.");
        var profile = Profile(profileId);
        var current = CodexInstallation.Home(profile);
        var canonical = Directory.Exists(home) ? FolderPaths.Canonicalize(home) : Path.GetFullPath(home);
        if (!FolderPaths.Comparer.Equals(canonical, current) && !Directory.Exists(canonical)) throw new IOException("Choose an existing Codex home, or keep the current home.");
        foreach (var other in State.Profiles.Where(other => other.Id != profileId))
        {
            var otherHome = CodexInstallation.Home(other);
            if (Directory.Exists(otherHome)) otherHome = FolderPaths.Canonicalize(otherHome);
            if (FolderPaths.Comparer.Equals(canonical, otherHome)) throw new InvalidOperationException("That Codex home is already connected to another profile.");
        }
        var changedHome = !FolderPaths.Comparer.Equals(Path.TrimEndingDirectorySeparator(current), Path.TrimEndingDirectorySeparator(canonical));
        Commit(state =>
        {
            var target = Profile(state, profileId); target.Name = name.Trim();
            if (changedHome) { target.ExistingCodexHome = canonical; target.Projects.Clear(); target.CustomProjectOrder.Clear(); target.OrderProjectsByLastActive = true; target.SelectedModel = null; }
        });
    }

    public Guid AddChat(Guid profileId, Guid projectId, ChatState? source = null)
    {
        EnsureProjectAvailable(profileId, projectId);
        var chat = new ChatState { Name = source is null ? "New chat" : source.Name + " (copy)" };
        Commit(state =>
        {
            var project = Project(state, profileId, projectId);
            project.IsArchived = false;
            project.Chats.Add(chat);
        });
        return chat.Id;
    }

    public void EditChat(Guid profileId, Guid projectId, Guid chatId, Action<ChatState> change)
    {
        EnsureProjectAvailable(profileId, projectId);
        Commit(state =>
        {
            var project = Project(state, profileId, projectId);
            var chat = project.Chats.Single(chat => chat.Id == chatId);
            change(chat);
            if (!chat.IsArchived) project.IsArchived = false;
        });
    }

    public void DeleteChat(Guid profileId, Guid projectId, Guid chatId) =>
        Commit(state => Project(state, profileId, projectId).Chats.RemoveAll(chat => chat.Id == chatId));

    public (Guid ProjectId, Guid ChatId) RegisterSession(SessionListItem session)
    {
        var profile = Profile(session.ProfileId);
        foreach (var project in profile.Projects.OrderByDescending(project => project.Id == session.ProjectId))
        {
            var known = project.Chats.FirstOrDefault(chat => session.ChatId == chat.Id || session.ThreadId is not null && chat.CodexThreadId == session.ThreadId);
            if (known is not null)
            {
                EnsureProjectAvailable(session.ProfileId, project.Id);
                if (session.CodexProjectId is { } remote && project.CodexProjectId != remote)
                {
                    var destinationId = RegisterProject(session.ProfileId, new(remote, session.ProjectName, session.Folders, 0));
                    if (destinationId == project.Id) return (destinationId, known.Id);
                    Commit(state =>
                    {
                        var source = Project(state, session.ProfileId, project.Id);
                        var chat = source.Chats.Single(chat => chat.Id == known.Id); source.Chats.Remove(chat);
                        Project(state, session.ProfileId, destinationId).Chats.Add(chat);
                    });
                    return (destinationId, known.Id);
                }
                if (session.CodexProjectId is null && (project.IsFolderGroup != session.IsUnassigned || !FolderPaths.SameFolder(project.PrimaryFolder, session.PrimaryFolder)))
                {
                    var destinationId = session.ProjectId ?? FolderGroup(session.ProfileId, session.PrimaryFolder);
                    if (destinationId == project.Id) return (destinationId, known.Id);
                    EnsureProjectAvailable(session.ProfileId, destinationId);
                    Commit(state =>
                    {
                        var source = Project(state, session.ProfileId, project.Id);
                        var chat = source.Chats.Single(chat => chat.Id == known.Id); source.Chats.Remove(chat);
                        Project(state, session.ProfileId, destinationId).Chats.Add(chat);
                    });
                    return (destinationId, known.Id);
                }
                return (project.Id, known.Id);
            }
        }
        if (session.ThreadId is null) throw new InvalidOperationException("This draft no longer exists.");
        if (session.Folders.Count == 0 || session.Folders.Any(folder => !Path.IsPathFullyQualified(folder)))
            throw new InvalidOperationException("This session has no usable working folder. Add its folder in the owning account before opening it.");
        var folders = session.Folders.Select(FolderPaths.Canonicalize).Distinct(FolderPaths.Comparer).ToList();
        var primary = FolderPaths.Canonicalize(session.PrimaryFolder);
        var target = profile.Projects.FirstOrDefault(project => project.Id == session.ProjectId)
            ?? (session.CodexProjectId is { } remoteId ? profile.Projects.FirstOrDefault(project => project.CodexProjectId == remoteId)
                : profile.Projects.FirstOrDefault(project => project.CodexProjectId is null && project.IsFolderGroup == session.IsUnassigned
                    && FolderPaths.SameFolder(project.PrimaryFolder, primary) && project.Folders.Count == folders.Count
                    && folders.All(folder => project.Folders.Any(root => FolderPaths.SameFolder(root, folder)))));
        var projectId = target?.Id ?? Guid.NewGuid();
        if (target is not null) EnsureProjectAvailable(session.ProfileId, target.Id);
        var newChat = new ChatState { Name = session.Name, CodexThreadId = session.ThreadId, NativeProjectId = session.NativeProjectId, IsArchived = session.IsArchived, IsPinned = session.IsPinned, CreatedAt = session.UpdatedAt, WorkingDirectory = session.WorkingDirectory };
        Commit(state =>
        {
            var owner = Profile(state, session.ProfileId);
            var destination = owner.Projects.FirstOrDefault(item => item.Id == projectId);
            if (destination is null)
            {
                destination = new ProjectState { Id = projectId, Name = session.ProjectName, PrimaryFolder = primary, Folders = folders, CodexProjectId = session.CodexProjectId, IsUnassigned = session.IsUnassigned && FolderPaths.IsDefaultChatFolder(primary), IsImportedFolder = session.CodexProjectId is null };
                owner.Projects.Add(destination);
            }
            destination.Chats.Add(newChat);
        });
        return (projectId, newChat.Id);
    }

    public Guid RegisterProject(Guid profileId, CodexProject project)
    {
        if (Profile(profileId).Projects.FirstOrDefault(item => item.CodexProjectId == project.Id) is { } existing)
        { EnsureProjectAvailable(profileId, existing.Id); return existing.Id; }
        var folders = project.Folders.Select(FolderPaths.Canonicalize).Distinct(FolderPaths.Comparer).ToList();
        if (folders.Count == 0) throw new IOException("This Codex project has no local folders.");
        var created = new ProjectState { Name = project.Name, Folders = folders, PrimaryFolder = folders[0], CodexProjectId = project.Id };
        Commit(state => Profile(state, profileId).Projects.Add(created));
        return created.Id;
    }

    public void LinkCodexProjects(Guid profileId, IReadOnlyList<CodexProject> projects)
    {
        var links = new Dictionary<Guid, CodexProject>();
        foreach (var local in Profile(profileId).Projects)
        {
            if (local.IsFolderGroup && local.CodexProjectId is null || IsDeletingProject(profileId, local.Id) || IsArchivingProject(profileId, local.Id)) continue;
            if (ProjectMatching.RemoteFor(local, projects) is { } remote && (local.CodexProjectId != remote.Id || local.Name != remote.Name
                || remote.Folders.Any(folder => !local.Folders.Any(root => FolderPaths.SameFolder(root, folder))
                    && !local.ExcludedCodexFolders.Any(root => FolderPaths.SameFolder(root, folder)))))
                links[local.Id] = remote;
        }
        if (links.Count == 0) return;
        Commit(state =>
        {
            foreach (var local in Profile(state, profileId).Projects)
                if (links.TryGetValue(local.Id, out var remote))
                {
                    foreach (var folder in remote.Folders)
                        if (!local.Folders.Any(root => FolderPaths.SameFolder(root, folder))
                            && !local.ExcludedCodexFolders.Any(root => FolderPaths.SameFolder(root, folder))) local.Folders.Add(folder);
                    local.CodexProjectId = remote.Id; local.Name = remote.Name;
                }
        });
    }
}
