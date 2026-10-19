using System.Text.Json;
using MiniCodex.Models;
using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed class MessageEdit
{
    public string MessageId { get; }
    public string TurnId { get; }
    public string OriginalText { get; }
    public bool HistoryReverted { get; internal set; }
    internal string? RetainedTurnId { get; set; }
    internal MessageEdit(ChatMessage message)
    {
        if (!message.CanEdit || message.TurnId is not { } turn) throw new InvalidOperationException(message.EditHint);
        MessageId = message.Id; TurnId = turn; OriginalText = message.Text;
    }
}

public sealed partial class AccountRuntime
{
    public MessageEdit PrepareMessageEdit(ChatMessage message) => new(message);

    private static void RememberFirstUser(ConversationState state, string turnId, string firstId)
    {
        state.FirstUserMessageIds[turnId] = firstId;
        foreach (var message in state.Messages.Where(message => message.Role == "You" && message.TurnId == turnId))
            message.SetTurnInput(turnId, message.Id != firstId);
    }

    private async Task<JsonElement> FirstUserItemAsync(string threadId, string turnId)
    {
        string? cursor = null;
        var cursors = new HashSet<string>();
        do
        {
            var page = await Client.CallAsync("thread/items/list", new { threadId, turnId, cursor, sortDirection = "asc", limit = 20 });
            foreach (var entry in page.Get("data").Items())
            {
                var item = entry.Get("item");
                if (item.Text("type") == "userMessage") return item;
                // Reviews can receive steering without an initial user message.
                if (item.Text("type") == "enteredReviewMode") return default;
            }
            cursor = page.Text("nextCursor");
            if (cursor is not null && !cursors.Add(cursor)) throw new IOException("Codex repeated a message history cursor.");
        } while (cursor is not null);
        return default;
    }

    private async Task LoadMessageMetadataAsync(ConversationState state)
    {
        if (state.ThreadId is not { } thread) return;
        var turns = state.Messages.Where(message => message.Role == "You" && message.TurnId is not null)
            .Select(message => message.TurnId!).Distinct().Where(turn => !state.FirstUserMessageIds.ContainsKey(turn)).ToArray();
        foreach (var turn in turns)
        {
            try { RememberFirstUser(state, turn, (await FirstUserItemAsync(thread, turn)).Text("id") ?? ""); }
            catch (IOException) { Notice = "Some message details could not be loaded. Refresh chat before editing those messages."; break; }
        }
    }

    private static string UserMessageText(JsonElement item)
    {
        var content = item.Get("content").Items().ToArray();
        var text = string.Join("\n", content.Select(part => part.Text("text")).OfType<string>());
        var parsed = MessagePayload.Parse(text);
        var images = content.Where(part => part.Text("type") == "localImage").Select(part => part.Text("path")).OfType<string>()
            .Where(Path.IsPathFullyQualified).Where(path => !parsed.Attachments.Any(attachment => FolderPaths.Comparer.Equals(attachment.Path, path)))
            .Select(path => new ChatAttachment(path, true)).ToArray();
        return images.Length == 0 ? text : MessagePayload.Compose(parsed.Body, parsed.Annotations, parsed.Attachments.Concat(images).Take(20).ToArray());
    }

    private static void ValidateMessageEdit(ConversationState state, MessageEdit edit)
    {
        if (state.UsesTurnHistory) throw new InvalidOperationException("Editing requires a chat with paginated history. This chat's history was preserved.");
        if (!edit.HistoryReverted && !state.Messages.Any(message => message.Id == edit.MessageId && message.Text == edit.OriginalText && message.CanEdit && message.TurnId == edit.TurnId))
            throw new InvalidOperationException("This message changed or is no longer available. Refresh chat and reopen Edit.");
    }

    private async Task<string?> LatestTurnIdAsync(string threadId)
    {
        var page = await Client.CallAsync("thread/turns/list", new { threadId, limit = 1, sortDirection = "desc", itemsView = "notLoaded" });
        if (page.Get("data").ValueKind != JsonValueKind.Array) throw new IOException("Codex did not return the retained history checkpoint. Refresh chat before retrying.");
        return page.Get("data").Items().FirstOrDefault().Text("id");
    }

    private async Task RevertEditedMessageAsync(ConversationState state, MessageEdit edit, Action submitted)
    {
        var threadId = state.ThreadId ?? throw new InvalidOperationException("This chat has no saved history.");
        ValidateMessageEdit(state, edit);
        if (edit.HistoryReverted)
        {
            if (await LatestTurnIdAsync(threadId) != edit.RetainedTurnId)
                throw new InvalidOperationException("New messages were added after the edit. Refresh chat before sending again.");
            return;
        }
        var first = await FirstUserItemAsync(threadId, edit.TurnId);
        RememberFirstUser(state, edit.TurnId, first.Text("id") ?? "");
        if (first.Text("id") != edit.MessageId || UserMessageText(first) != edit.OriginalText)
            throw new InvalidOperationException("This message changed or was sent with Steer. Refresh chat before editing.");
        var index = state.Messages.ToList().FindIndex(message => message.Id == edit.MessageId);
        if (index < 0) throw new InvalidOperationException("This message is no longer available. Refresh chat before editing.");
        submitted();
        var result = await Client.CallAsync("thread/revert", new { threadId, beforeTurnId = edit.TurnId });
        edit.HistoryReverted = true;
        if (result.Get("thread").Text("id") != threadId || !result.TryGetProperty("itemsBackwardsCursor", out _))
            throw new IOException("Codex did not return the expected updated history. Refresh chat before retrying.");
        for (var position = state.Messages.Count - 1; position >= index; position--) state.Messages.RemoveAt(position);
        state.RefreshTranscript();
        state.HistoryCursor = result.Text("itemsBackwardsCursor");
        state.ContextUsed = null; state.LastTurnStatus = null;
        state.FirstUserMessageIds.Clear();
        foreach (var message in state.Messages.Where(message => message.CanEdit && message.TurnId is not null))
            state.FirstUserMessageIds[message.TurnId!] = message.Id;
        // Retain a checkpoint so retrying a rejected turn/start cannot erase later messages.
        edit.RetainedTurnId = await LatestTurnIdAsync(threadId);
        SessionListChanged?.Invoke(null);
        Notify();
    }
}
