using System.Text.Json;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    public string SessionActivity(Guid? chatId, string? threadId)
    {
        var state = chatId is { } id ? _conversations.GetValueOrDefault(id) : Find(threadId);
        return state is not null ? TaskActivity(state) : IsSessionPaused(chatId, threadId) ? "Paused" : "Ready";
    }

    public string TaskActivity(ConversationState state)
    {
        if (state.Uncertain || state.ConflictPause is not null) return state.Status;
        var finished = state.TaskFinishedAt is not null && state.Terminal.Task.IsCompleted;
        if ((!state.Busy || finished) && (state.LastTurnStatus is "failed" or "interrupted"))
            return state.LastTurnStatus == "failed" ? "Failed" : "Stopped";
        if ((Connected || Suspended) && state.ThreadId is { } threadId)
        {
            var requests = Approvals.Where(request => request.ThreadId == threadId).ToArray();
            if (requests.Any(request => request.Method.Contains("requestUserInput", StringComparison.Ordinal))) return "Waiting for your answer";
            if (requests.Length > 0) return "Waiting for approval";
            if (state.Messages.Any(message => message.Role != "You" && message.CanAnswer)) return "Waiting for your answer";
        }
        if (!state.Busy || finished) return state.LastTurnStatus == "completed" ? "Completed" : state.Status;
        if (state.IsCompacting) return "Compacting";
        return state.Activity switch
        {
            ConversationActivityKind.Command => "Running command",
            ConversationActivityKind.FileChange => "Editing files",
            ConversationActivityKind.WebSearch => "Searching web",
            ConversationActivityKind.Tool => "Using tool",
            ConversationActivityKind.Response => "Writing response",
            _ => state.Reviewing ? "Reviewing" : "Thinking"
        };
    }

    private static bool IsCurrentTurn(ConversationState state, string? turnId) => state.Busy && !state.Uncertain
        && (turnId is null || state.TurnId is null || state.TurnId == turnId);

    private static void TrackTaskActivity(ConversationState state, JsonElement item, string? turnId, bool completed)
    {
        if (!IsCurrentTurn(state, turnId)) return;
        var type = item.Text("type");
        if (type == "enteredReviewMode") state.Reviewing = true;
        if (type == "exitedReviewMode" && completed) state.Reviewing = false;
        var id = item.Text("id");
        if (completed) { state.EndActivity(id); return; }
        ConversationActivityKind? kind = type switch
        {
            "commandExecution" => ConversationActivityKind.Command,
            "fileChange" => ConversationActivityKind.FileChange,
            "webSearch" => ConversationActivityKind.WebSearch,
            "mcpToolCall" or "dynamicToolCall" or "collabToolCall" or "imageView" or "imageGeneration" => ConversationActivityKind.Tool,
            "agentMessage" or "exitedReviewMode" => ConversationActivityKind.Response,
            _ => null
        };
        if (kind is { } activity) state.BeginActivity(id, activity);
    }
}
