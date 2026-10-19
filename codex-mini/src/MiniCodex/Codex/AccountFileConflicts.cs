using System.Text.Json;
using MiniCodex.Models;
using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    internal IEnumerable<ConversationState> Conversations => _conversations.Values;
    internal event Action<ConversationState, IReadOnlyList<string>>? FilesEdited;
    internal Func<FileConflictPause, bool>? ConflictIsBlocked { get; set; }

    private static void BeginFileTracking(ConversationState state, string directory)
    {
        state.TaskRunId = Guid.NewGuid(); state.ActiveDirectory = directory;
        state.EditedFiles.Clear(); state.LastTurnStatus = null;
        state.TaskFinishedAt = null; state.ClearActivities();
    }
    private static void RequireConflictResolved(ConversationState state, bool continuing = false)
    {
        if (state.ConflictPause is not null || state.ContinuingConflict && !continuing)
            throw new InvalidOperationException("This task is paused after a file conflict. Choose Continue or Cancel in the chat first.");
    }
    private void TrackFileChanges(ConversationState state, JsonElement item, string? turnId)
    {
        // Notifications identify reported edits, not a before-write lock. Shell/script writes
        // without fileChange items are outside this best-effort tracker.
        if (!state.Busy || state.Uncertain || state.LastTurnStatus is not null || item.Text("type") != "fileChange" || item.Text("status") is "failed" or "declined"
            || turnId is not null && state.TurnId is not null && turnId != state.TurnId) return;
        var directory = state.ActiveDirectory;
        if (!Path.IsPathFullyQualified(directory))
        {
            var project = controller.Profile(ProfileId).Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == state.LocalId));
            directory = project?.Chats.FirstOrDefault(chat => chat.Id == state.LocalId)?.WorkingDirectory ?? project?.PrimaryFolder ?? "";
        }
        var added = new List<string>();
        foreach (var change in item.Get("changes").Items())
            foreach (var path in new[] { change.Text("path"), change.Get("kind").Text("movePath"), change.Text("movePath") }.OfType<string>())
            {
                try
                {
                    var full = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, directory);
                    // Resolve existing parent directories so junction aliases share a claim;
                    // deleted files and files in new directories can still be tracked.
                    var parent = Path.GetDirectoryName(full);
                    while (parent is not null && !Directory.Exists(parent)) parent = Path.GetDirectoryName(parent);
                    if (parent is not null)
                    {
                        try { full = Path.GetFullPath(Path.GetRelativePath(parent, full), FolderPaths.Canonicalize(parent)); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Keep the absolute reported path if alias resolution is unavailable. */ }
                    }
                    if (state.EditedFiles.Add(full)) added.Add(full);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
            }
        if (added.Count > 0) FilesEdited?.Invoke(state, added);
    }
    public bool IsSessionPaused(Guid? chatId, string? threadId)
    {
        var state = chatId is { } id ? _conversations.GetValueOrDefault(id) : Find(threadId);
        if (state is not null) return state.IsConflictPaused;
        return controller.Profile(ProfileId).Projects.SelectMany(project => project.Chats)
            .Any(chat => (chatId is { } local ? chat.Id == local : threadId is not null && chat.CodexThreadId == threadId) && chat.ConflictPause is not null);
    }
    private void SaveConflictPause(ConversationState state)
    {
        var project = controller.Profile(ProfileId).Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == state.LocalId));
        if (project is null) return;
        controller.EditChat(ProfileId, project.Id, state.LocalId, chat => chat.ConflictPause = state.ConflictPause);
    }
    internal void RefreshConflictAvailability()
    {
        var changed = false;
        foreach (var state in _conversations.Values.Where(state => state.ConflictPause is not null))
        {
            var blocked = ConflictIsBlocked?.Invoke(state.ConflictPause!) == true;
            if (state.ConflictBlocked == blocked) continue;
            state.ConflictBlocked = blocked; changed = true;
        }
        if (changed) Notify();
    }
    internal void PauseForFileConflict(ConversationState state, IReadOnlyList<string> files, FileConflictBlocker blocker)
    {
        if (!state.Busy || state.Uncertain) return;
        var previous = state.ConflictPause;
        var paths = (previous?.Files ?? []).Concat(files).Distinct(FolderPaths.Comparer).OrderBy(path => path, FolderPaths.Comparer).ToArray();
        var blockers = (previous?.Blockers ?? []).Append(blocker).DistinctBy(item => (item.ProfileId, item.ChatId, item.TaskRunId)).ToArray();
        if (previous is not null && paths.SequenceEqual(previous.Files, FolderPaths.Comparer) && blockers.SequenceEqual(previous.Blockers)) return;
        state.ConflictPause = new(paths, blockers); state.ConflictBlocked = true;
        state.QueuePaused = true; state.QueueNotice = "Queue paused after a file conflict. Choose Continue or Cancel in the chat.";
        var alreadyStopping = state.PausingForConflict;
        state.PausingForConflict = true;
        try { SaveConflictPause(state); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Report(new IOException("The file-conflict pause could not be saved. Keep this account open. " + error.Message)); }
        Notify();
        if (alreadyStopping) return;
        state.Info("File conflict detected: " + string.Join(", ", paths.Select(Path.GetFileName)) + ". Stopping this task; existing file changes are kept.");
        Observe(async () =>
        {
            try { await StopAsync(state); }
            catch (Exception error)
            {
                // Never present a confirmed pause when cancellation was not acknowledged.
                if (state.Busy) state.Uncertain = true;
                state.Info("Could not confirm the task stopped after a file conflict. Reconnect and Refresh chat before continuing. " + error.Message);
                Report(error);
            }
            finally
            {
                state.PausingForConflict = false;
                state.QueuePaused = true; state.QueueNotice = "Queue paused after a file conflict. Choose Continue or Cancel in the chat.";
                Notify();
            }
        });
    }
    public bool CanContinueConflict(ConversationState state) => state.ConflictPause is { } pause && !state.Busy && !state.Uncertain && !state.Loading
        && !state.PausingForConflict && !state.ContinuingConflict && !state.QueueDispatching && !state.QueueEditing
        && ConflictIsBlocked?.Invoke(pause) != true;
    public async Task ContinueConflictAsync(ProjectState project, ChatState chat, CodexModel model, string effort, string approvalMode, bool fastMode)
    {
        RequireNoExtensionInstall();
        var state = Conversation(chat);
        if (!CanContinueConflict(state) || state.ConflictPause is not { } pause)
            throw new InvalidOperationException("Wait for the conflicting task to finish and for Codex to confirm this chat is idle before continuing.");
        state.ContinuingConflict = true; Notify();
        var accepted = false;
        try
        {
            if (!Connected) await ConnectAsync();
            if (state.Busy || state.Uncertain || ConflictIsBlocked?.Invoke(pause) == true)
                throw new InvalidOperationException("The conflicting task is still active. Wait before continuing.");
            var prompt = "Continue the unfinished task from the interrupted turn. It stopped after a file conflict with another task. "
                + "Before editing, reread the current contents of these files and account for changes made since the interruption:\n"
                + string.Join("\n", pause.Files) + "\nPreserve existing changes and finish the remaining work without repeating completed steps.";
            state.ConflictPause = null;
            await SendAsync(project, chat, prompt, model, effort, approvalMode, fastMode: fastMode, continuingConflict: true);
            accepted = true;
            if (state.ConflictPause is null)
            {
                try { SaveConflictPause(state); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                { Report(new IOException("The continuation started, but its saved pause state could not be cleared. Refresh this chat before continuing again after a restart. " + error.Message)); }
                if (!state.Uncertain && (state.Busy || state.LastTurnStatus == "completed"))
                {
                    state.QueuePaused = false; state.QueueNotice = "";
                }
            }
        }
        catch
        {
            // Submission failures and unknown outcomes retain the pause for reconciliation.
            if (!accepted) state.ConflictPause ??= pause;
            state.QueuePaused = true;
            throw;
        }
        finally
        {
            state.ContinuingConflict = false; Notify();
            if (accepted && state.ConflictPause is null && !state.QueuePaused && !state.Busy && !state.Uncertain && state.LastTurnStatus == "completed")
                Observe(() => DrainQueueAsync(state));
        }
    }
    public void CancelConflict(ConversationState state)
    {
        if (state.ConflictPause is null) return;
        if (state.Busy || state.Uncertain || state.PausingForConflict || state.ContinuingConflict || state.QueueDispatching)
            throw new InvalidOperationException("Confirm this task has stopped before canceling its paused state. Use Refresh chat after a connection loss.");
        var pause = state.ConflictPause; state.ConflictPause = null;
        try { SaveConflictPause(state); }
        catch { state.ConflictPause = pause; throw; }
        state.ConflictBlocked = false; state.QueuePaused = true;
        state.QueueNotice = "Queue paused after canceling the task. Resume it explicitly to send queued messages.";
        state.Info("Paused task canceled. Existing file changes and conversation history were kept.");
        Notify();
    }
}
