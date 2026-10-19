using MiniCodex.Models;
using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    public bool HasQueuedMessages => drafts.For(ProfileId).Queues.Values.Any(queue => queue.Count > 0);
    public int QueueCountFor(Guid? chatId, string? threadId) => (chatId ?? Find(threadId)?.LocalId) is { } id ? drafts.For(ProfileId).Queues.GetValueOrDefault(id)?.Count ?? 0 : 0;
    public void PauseQueue(ConversationState state, string reason = "Queue paused")
    { state.QueuePaused = true; state.QueueNotice = reason; Notify(); }
    private void PauseAllQueues(string reason)
    { foreach (var state in _conversations.Values) if (state.Queue.Count > 0) { state.QueuePaused = true; state.QueueNotice = reason; } }
    public void Enqueue(ProjectState project, ChatState chat, QueuedMessage message)
    {
        if (chat.IsArchived) throw new InvalidOperationException("Restore this chat before queueing a message.");
        var state = Conversation(chat);
        if (state.Queue.Count >= 50) throw new InvalidOperationException("Queue up to 50 messages per chat.");
        var workspace = controller.WorkspaceForChat(ProfileId, project.Id, chat.Id);
        _ = BuildInput(message.Text, message.References, workspace.Folders, message.Attachments.Select(file => file.Open()).ToArray());
        state.Queue.Add(message); drafts.QueueChanged(message); Touch(); Notify();
        if (!state.Busy && !state.QueuePaused) Observe(() => DrainQueueAsync(state));
    }
    public void RemoveQueued(ConversationState state, QueuedMessage message)
    {
        if (state.QueueEditing) throw new InvalidOperationException("Finish editing the queued message first.");
        if (state.QueueDispatching && state.Steering) throw new InvalidOperationException("Wait for queued steering to finish before removing messages.");
        if (state.QueueDispatching && state.Queue.FirstOrDefault() == message) throw new InvalidOperationException("This message is being submitted. Wait for its acknowledgement.");
        state.Queue.Remove(message); drafts.QueueChanged(); Notify();
    }
    public void MoveQueued(ConversationState state, QueuedMessage message, int offset)
    {
        if (state.QueueEditing) throw new InvalidOperationException("Finish editing the queued message first.");
        if (state.QueueDispatching) throw new InvalidOperationException("Wait for the pending message to be submitted before reordering.");
        var index = state.Queue.IndexOf(message); var target = index + offset;
        if (index < 0 || target < 0 || target >= state.Queue.Count) return;
        state.Queue.Move(index, target); drafts.QueueChanged(); Notify();
    }
    public async Task ResumeQueueAsync(ConversationState state)
    {
        RequireNoExtensionInstall();
        RequireConflictResolved(state);
        if (state.QueueEditing) throw new InvalidOperationException("Finish editing the queued message before resuming.");
        if (state.Uncertain) throw new InvalidOperationException("Refresh this chat's state before resuming its queue.");
        if (state.Queue.Any(item => item.MayHaveBeenSent)) throw new InvalidOperationException("A queued message may already have been sent. Check history, then remove it or choose Send again.");
        state.QueuePaused = false; state.QueueNotice = ""; Touch(); Notify();
        await DrainQueueAsync(state);
    }
    public bool CanSteerQueued(ConversationState state, QueuedMessage message) => Connected && state.Busy && state.TurnId is not null
        && !state.Uncertain && !state.IsCompacting && !state.Steering && !state.QueueDispatching && !state.QueueEditing
        && !message.MayHaveBeenSent && state.Queue.Contains(message);
    public async Task SteerQueuedAsync(ConversationState state, QueuedMessage message)
    {
        if (!CanSteerQueued(state, message)) throw new InvalidOperationException("Select an unsent queued message while a task is running. Wait for submission or editing to finish before steering.");
        var project = controller.Profile(ProfileId).Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == state.LocalId))
            ?? throw new InvalidOperationException("This queued message's project is unavailable.");
        var chat = project.Chats.Single(chat => chat.Id == state.LocalId);
        state.QueueDispatching = true; Touch(); Notify();
        try
        {
            message.MayHaveBeenSent = true; drafts.QueueChanged(); drafts.Flush();
            await SteerAsync(project, chat, message.Text, message.References, message.Attachments.Select(file => file.Open()).ToArray());
            state.Queue.Remove(message); drafts.QueueChanged(); drafts.Flush();
        }
        catch (Exception error)
        {
            if (!state.Uncertain) { message.MayHaveBeenSent = false; drafts.QueueChanged(); }
            PauseQueue(state, error.Message); Report(error); throw;
        }
        finally
        {
            state.QueueDispatching = false; Notify();
            if (!state.Busy && !state.QueuePaused && !state.Uncertain && state.Queue.Count > 0) Observe(() => DrainQueueAsync(state));
        }
    }
    private async Task DrainQueueAsync(ConversationState state)
    {
        if (InstallingExtensions || state.ConflictPause is not null || state.ContinuingConflict || state.Busy || state.Loading || state.Uncertain || state.QueuePaused || state.QueueDispatching || state.QueueEditing || state.Queue.Count == 0) return;
        state.QueueDispatching = true; Notify();
        var item = state.Queue[0];
        try
        {
            if (!Connected) await ConnectAsync();
            if (state.QueuePaused || state.Uncertain) return;
            if (item.MayHaveBeenSent) throw new InvalidOperationException("Check history before sending this recovered message again.");
            var project = controller.Profile(ProfileId).Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == state.LocalId))
                ?? throw new InvalidOperationException("This queued message's project is unavailable.");
            var chat = project.Chats.Single(chat => chat.Id == state.LocalId);
            var model = Models.FirstOrDefault(model => model.Id == item.ModelId) ?? throw new InvalidOperationException("The queued model is unavailable. Choose an available model through Edit in the message queue.");
            item.MayHaveBeenSent = true; drafts.QueueChanged(); drafts.Flush();
            await SendAsync(project, chat, item.Text, model, item.Effort, item.ApprovalMode, item.References, item.Attachments.Select(file => file.Open()).ToArray(), item.FastMode);
            state.Queue.Remove(item); drafts.QueueChanged(); drafts.Flush();
        }
        catch (Exception error)
        {
            if (error is CodexRpcException) { item.MayHaveBeenSent = false; drafts.QueueChanged(); }
            PauseQueue(state, error.Message); Report(error);
        }
        finally
        {
            state.QueueDispatching = false; Notify();
            if (!state.Busy && !state.QueuePaused && !state.Uncertain && state.Queue.Count > 0) Observe(() => DrainQueueAsync(state));
        }
    }
    public async Task SteerAsync(ProjectState project, ChatState chat, string text, IReadOnlyList<ComposerReference> references, IReadOnlyList<ChatAttachment> attachments)
    {
        controller.EnsureProjectAvailable(ProfileId, project.Id);
        var state = Conversation(chat);
        RequireConflictResolved(state);
        if (!Connected || !state.Busy || state.Uncertain || state.IsCompacting || state.Steering || state.TurnId is not { } turn)
            throw new InvalidOperationException("Wait for an active task before steering it. Compacting tasks cannot be steered.");
        var folders = state.ActiveFolders.Count > 0 ? state.ActiveFolders : controller.WorkspaceForChat(ProfileId, project.Id, chat.Id).Folders;
        var input = BuildInput(text, references, folders, attachments);
        state.Steering = true; Touch(); Notify();
        try { await Client.CallAsync("turn/steer", new { threadId = state.ThreadId, expectedTurnId = turn, input }); }
        catch (Exception error) when (error is not CodexRpcException)
        { state.Uncertain = true; PauseQueue(state, "Queue paused because the steering outcome is unknown."); state.Info("The steering outcome is unknown; it was not retried."); throw; }
        finally { state.Steering = false; Notify(); }
    }
    public async Task ReviewAsync(ProjectState project, ChatState chat, CodexModel model, string approvalMode, bool fastMode, object target)
    {
        RequireNoExtensionInstall();
        if (!Connected) await ConnectAsync();
        var state = Conversation(chat);
        RequireConflictResolved(state);
        if (!SignedIn || chat.IsArchived || state.Busy || state.Loading || state.Uncertain || state.QueueDispatching)
            throw new InvalidOperationException("Open an idle, active chat before starting code review.");
        if (state.ThreadId is not null && !state.Loaded) await LoadChatAsync(project, chat);
        RequireNoExtensionInstall();
        RequireConflictResolved(state);
        if (state.Busy || state.Uncertain || state.Loading) throw new InvalidOperationException("This chat is busy. Wait before starting code review.");
        var workspace = controller.WorkspaceForChat(ProfileId, project.Id, chat.Id);
        BeginFileTracking(state, workspace.PrimaryDirectory);
        controller.MarkProjectTaskStarted(ProfileId, project.Id);
        state.TaskStartedAt = DateTimeOffset.UtcNow;
        state.Submitting = true; state.ActiveFolders = workspace.Folders; Touch(); Notify();
        var reviewSent = false;
        try
        {
            var setup = new Dictionary<string, object?> { ["cwd"] = workspace.PrimaryDirectory, ["model"] = model.Id,
                ["approvalPolicy"] = "on-request", ["sandbox"] = "workspace-write", ["config"] = Config(workspace, approvalMode, fastMode), ["excludeTurns"] = true };
            if (state.ThreadId is null)
            {
                if (project.CodexProjectId is not null) setup["projectId"] = project.CodexProjectId;
                var started = await Client.CallAsync("thread/start", setup);
                state.ThreadId = started.Get("thread").Text("id") ?? throw new IOException("Codex did not return a review thread ID.");
                SessionListChanged?.Invoke(state.ThreadId);
                controller.EditChat(ProfileId, project.Id, chat.Id, saved => { saved.CodexThreadId = state.ThreadId; saved.NativeProjectId = project.CodexProjectId; saved.Name = "Code review"; });
            }
            else
            {
                setup["threadId"] = state.ThreadId;
                var resumed = await Client.CallAsync("thread/resume", setup);
                if (resumed.Get("thread").Get("status").Text("type") == "active") { state.Uncertain = true; throw new InvalidOperationException("This chat is active in another client. Stop it there and Refresh chat."); }
            }
            state.Loaded = true; state.Reviewing = true;
            state.Terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            reviewSent = true;
            var response = await Client.CallAsync("review/start", new { threadId = state.ThreadId, delivery = "inline", target });
            if (!state.Terminal.Task.IsCompleted) state.TurnId = response.Get("turn").Text("id");
        }
        catch (Exception error)
        {
            PauseQueue(state, "Queue paused because review could not start: " + error.Message);
            if (reviewSent && error is not CodexRpcException) { state.Uncertain = true; state.Info("Review outcome unknown. Refresh before retrying."); }
            else { state.Reviewing = false; state.LastTurnStatus = "failed"; state.TaskFinishedAt = DateTimeOffset.UtcNow; state.ClearActivities(); state.Terminal.TrySetResult(); }
            throw;
        }
        finally { state.Submitting = false; Notify(); if (!state.Busy && !state.Uncertain && !state.QueuePaused && state.Queue.Count > 0) Observe(() => DrainQueueAsync(state)); }
    }
}
