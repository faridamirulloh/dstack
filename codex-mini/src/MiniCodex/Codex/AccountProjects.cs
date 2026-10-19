using System.Windows.Threading;
using MiniCodex.Models;
using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    private readonly Dictionary<Guid, (TaskCompletionSource Completion, bool Started)> _projectChatOperations = [];
    private readonly Dictionary<Guid, (Guid Project, string? Thread, TaskCompletionSource Completion, bool Started)> _archivingSessions = [];
    private readonly Dictionary<Guid, (Guid Project, string? Thread, TaskCompletionSource Completion, bool Started)> _deletingSessions = [];
    private readonly HashSet<string> _pendingArchiveRows = [];
    private readonly HashSet<string> _pendingDeleteRows = [];

    public bool IsSessionArchiving(Guid? chatId, string? threadId) => chatId is { } id && _archivingSessions.ContainsKey(id)
        || threadId is not null && _archivingSessions.Values.Any(operation => operation.Thread == threadId)
        || _pendingArchiveRows.Contains(ProfileId.ToString("N") + "/" + (threadId ?? chatId?.ToString("N")));

    public bool IsSessionDeleting(Guid? chatId, string? threadId) => chatId is { } id && _deletingSessions.ContainsKey(id)
        || threadId is not null && _deletingSessions.Values.Any(operation => operation.Thread == threadId)
        || _pendingDeleteRows.Contains(ProfileId.ToString("N") + "/" + (threadId ?? chatId?.ToString("N")));

    private void RequireNoSessionChanges(Guid? projectId)
    {
        if (_archivingSessions.Values.Concat(_deletingSessions.Values).Any(operation => projectId is null || operation.Project == projectId))
            throw new InvalidOperationException("Wait for chats to finish archiving or deleting before changing the project.");
    }

    public Task ArchiveOldChatsAsync(Guid projectId, SessionCatalog catalog) => ChangeProjectChatsAsync(projectId, catalog, false);
    public Task ArchiveProjectAsync(Guid projectId, SessionCatalog catalog) => ChangeProjectChatsAsync(projectId, catalog, false, archiveProject: true);
    public Task ArchivePreviousChatsAsync(Guid projectId, SessionListItem through, SessionCatalog catalog) => ChangeProjectChatsAsync(projectId, catalog, false, through);
    public Task DeleteArchivedChatsAsync(Guid? projectId, SessionCatalog catalog) => ChangeProjectChatsAsync(projectId, catalog, true);

    private async Task ChangeProjectChatsAsync(Guid? projectId, SessionCatalog catalog, bool deleteArchived, SessionListItem? through = null, bool archiveProject = false)
    {
        if (projectId is { } ownerId) controller.EnsureProjectAvailable(ProfileId, ownerId);
        RequireNoSessionChanges(projectId);
        var operationId = projectId ?? Guid.Empty;
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _projectChatOperations.Add(operationId, (operation, false));
        var started = false;
        var changed = 0;
        var verb = deleteArchived ? "deleted" : "archived";
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        string[] pendingSessionKeys = [];
        var pendingRows = deleteArchived ? _pendingDeleteRows : _pendingArchiveRows;
        try
        {
            if (deleteArchived)
            {
                if (projectId is { } deletingId) controller.BeginProjectDeletion(ProfileId, deletingId);
                else controller.BeginArchivedChatsDeletion(ProfileId);
            }
            else controller.BeginProjectArchiving(ProfileId, projectId!.Value);
            started = true;
            Notice = deleteArchived ? "Finding archived chats to delete…" : "Finding chats to archive…";
            Touch(); Notify();
            await Dispatcher.Yield(DispatcherPriority.Background);
            var members = await catalog.ProjectChatsAsync(ProfileId, projectId, archivedOnly: deleteArchived);
            if (members.Any(row => row.ProfileId != ProfileId || projectId is not null && row.ProjectId != projectId))
                throw new InvalidOperationException("The project chat list changed. Refresh before changing chats.");
            if (through is not null)
            {
                var selected = members.FirstOrDefault(row => row.Key == through.Key)
                    ?? throw new InvalidOperationException("The selected chat is no longer active in this project. Refresh the chat list.");
                cutoff = selected.UpdatedAt;
            }
            var targets = members.Where(row => deleteArchived ? row.IsArchived
                : !row.IsArchived && (archiveProject || (through is null ? row.UpdatedAt < cutoff : row.UpdatedAt <= cutoff))).ToArray();
            var chatIds = targets.Select(row => row.ChatId).OfType<Guid>().ToHashSet();
            var threadIds = targets.Select(row => row.ThreadId).OfType<string>().ToHashSet();
            var states = _conversations.Values.Where(state => chatIds.Contains(state.LocalId)
                || state.ThreadId is { } thread && threadIds.Contains(thread)).ToArray();
            if (targets.Any(row => row.IsActive) || states.Any(state => state.Busy || state.Uncertain || state.Loading || state.Steering || state.QueueDispatching || state.QueueEditing || state.PausingForConflict || state.ContinuingConflict))
                throw new InvalidOperationException("Finish editing, then stop or reconcile the matching chats' tasks before changing them.");
            if (targets.Any(row => row.ThreadId is not null)) await ConnectAsync();
            _projectChatOperations[operationId] = (operation, true);
            pendingSessionKeys = targets.Select(row => row.Key).ToArray();
            foreach (var key in pendingSessionKeys) pendingRows.Add(key);
            Notify();
            await Dispatcher.Yield(DispatcherPriority.Background);
            foreach (var state in states) PauseQueue(state, deleteArchived ? "Queue paused because this chat is being deleted." : "Queue paused because this chat is being archived.");
            for (var index = 0; index < targets.Length; index++)
            {
                var row = targets[index];
                Notice = $"{(deleteArchived ? "Deleting" : "Archiving")} chat {index + 1} of {targets.Length}…";
                Touch(); Notify();
                if (row.ThreadId is { } thread)
                {
                    await Client.CallAsync(deleteArchived ? "thread/delete" : "thread/archive", new { threadId = thread });
                    changed++;
                }
                var localIds = controller.Profile(ProfileId).Projects.SelectMany(project => project.Chats)
                    .Where(chat => chat.Id == row.ChatId || row.ThreadId is not null && chat.CodexThreadId == row.ThreadId).Select(chat => chat.Id).ToHashSet();
                if (localIds.Count > 0)
                    controller.Commit(next =>
                    {
                        foreach (var project in AppController.Profile(next, ProfileId).Projects)
                            if (deleteArchived) project.Chats.RemoveAll(chat => localIds.Contains(chat.Id));
                            else foreach (var chat in project.Chats.Where(chat => localIds.Contains(chat.Id))) chat.IsArchived = true;
                    });
                if (row.ThreadId is null) changed++;
                if (deleteArchived)
                    foreach (var chatId in localIds) _conversations.Remove(chatId);
                if (deleteArchived) catalog.Invalidate(ProfileId, row.ThreadId, reload: false);
                else catalog.MarkArchived(row);
                if (pendingRows.Remove(row.Key)) Notify();
            }
            if (archiveProject) controller.Commit(next => AppController.Project(next, ProfileId, projectId!.Value).IsArchived = true);
            Notice = archiveProject ? $"Project archived. {changed} {(changed == 1 ? "chat" : "chats")} archived."
                : targets.Length == 0 ? (deleteArchived ? "No archived chats to delete." : "No chats matched the archive cutoff.")
                : $"{changed} {(changed == 1 ? "chat" : "chats")} {verb}.";
        }
        catch (Exception error)
        {
            throw new IOException($"Could not finish {(deleteArchived ? "deleting archived chats" : "archiving chats")}. {changed} {(changed == 1 ? "chat" : "chats")} {verb}. {error.Message}", error);
        }
        finally
        {
            try
            {
                foreach (var key in pendingSessionKeys) pendingRows.Remove(key);
                if (started)
                {
                    if (!deleteArchived) controller.EndProjectArchiving(ProfileId, projectId!.Value);
                    else if (projectId is { } deletingId) controller.EndProjectDeletion(ProfileId, deletingId);
                    else controller.EndArchivedChatsDeletion(ProfileId);
                }
                drafts.Flush();
            }
            finally { _projectChatOperations.Remove(operationId); operation.TrySetResult(); Notify(); }
        }
    }

    public async Task DeleteProjectAsync(Guid projectId, SessionCatalog catalog)
    {
        controller.EnsureProjectAvailable(ProfileId, projectId);
        RequireNoSessionChanges(projectId);
        var project = AppController.Project(controller.State, ProfileId, projectId);
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _projectChatOperations.Add(projectId, (operation, false));
        var deleted = 0;
        var complete = false;
        string[] pendingDeleteKeys = [];
        try
        {
            controller.BeginProjectDeletion(ProfileId, projectId);
            Touch(); Notify();
            await Dispatcher.Yield(DispatcherPriority.Background);
            var members = await catalog.ProjectChatsAsync(ProfileId, projectId);
            if (members.Any(row => row.ProfileId != ProfileId || row.ProjectId != projectId))
                throw new InvalidOperationException("The project chat list changed. Refresh before deleting the project.");
            if (members.Any(row => row.IsActive)) throw new InvalidOperationException("Stop this project's running tasks before deleting it.");
            project = AppController.Project(controller.State, ProfileId, projectId);
            if (project.CodexProjectId is not null || members.Any(row => row.ThreadId is not null)) await ConnectAsync();
            project = AppController.Project(controller.State, ProfileId, projectId);
            var confirmedChats = members.Select(row => row.ChatId).OfType<Guid>().ToHashSet();
            var confirmedThreads = members.Select(row => row.ThreadId).OfType<string>().ToHashSet();
            if (project.Chats.Any(chat => !confirmedChats.Contains(chat.Id) && (chat.CodexThreadId is not { } thread || !confirmedThreads.Contains(thread))))
                throw new InvalidOperationException("New chats were added to this project. Open Delete project again to review the updated list.");
            var states = _conversations.Values.Where(state => confirmedChats.Contains(state.LocalId)
                || state.ThreadId is { } thread && confirmedThreads.Contains(thread)).ToArray();
            if (states.Any(state => state.Busy || state.Uncertain || state.Loading || state.Steering || state.QueueDispatching || state.QueueEditing))
                throw new InvalidOperationException("Finish editing, then stop or reconcile this project's tasks before deleting it. Tasks in other projects can keep running.");
            _projectChatOperations[projectId] = (operation, true);
            pendingDeleteKeys = members.Select(row => row.Key).ToArray();
            foreach (var key in pendingDeleteKeys) _pendingDeleteRows.Add(key);
            Notify();
            await Dispatcher.Yield(DispatcherPriority.Background);
            foreach (var state in states) PauseQueue(state, "Queue paused because this project is being deleted.");
            foreach (var row in members)
            {
                Touch();
                if (row.ThreadId is { } thread) await Client.CallAsync("thread/delete", new { threadId = thread });
                var localChats = AppController.Project(controller.State, ProfileId, projectId).Chats
                    .Where(chat => chat.Id == row.ChatId || row.ThreadId is not null && chat.CodexThreadId == row.ThreadId).ToArray();
                foreach (var chat in localChats)
                { controller.DeleteChat(ProfileId, projectId, chat.Id); _conversations.Remove(chat.Id); }
                catalog.Invalidate(ProfileId, row.ThreadId);
                if (_pendingDeleteRows.Remove(row.Key)) Notify();
                deleted++;
            }
            if (project.CodexProjectId is { } remote) await Client.CallAsync("project/delete", new { projectId = remote });
            controller.DeleteProject(ProfileId, projectId);
            complete = true;
        }
        catch (Exception error)
        {
            var progress = deleted == 0 ? "No chats were deleted." : $"{deleted} chat(s) were deleted; the project and remaining chats were kept.";
            throw new IOException($"Could not finish deleting '{project.Name}'. {progress} {error.Message}", error);
        }
        finally
        {
            try
            {
                foreach (var key in pendingDeleteKeys) _pendingDeleteRows.Remove(key);
                controller.EndProjectDeletion(ProfileId, projectId);
                catalog.ForgetProject(ProfileId, complete ? project.CodexProjectId : null);
                drafts.Flush();
            }
            finally { _projectChatOperations.Remove(projectId); operation.TrySetResult(); Notify(); }
        }
    }
}
