using System.Collections.ObjectModel;
using System.Text.Json;
using MiniCodex.Presentation;
using MiniCodex.Models;

namespace MiniCodex.Codex;

public sealed class ChatMessage(string id, string role, string text) : ObservableObject
{
    private string _text = Wire.Short(text, 128 * 1024);
    private bool _terminalExpanded;
    private bool _streaming;
    private bool? _steered;
    private ParsedMessage? _parsed;
    private QuestionTranscript? _questions;
    private QuestionTranscript? _questionMetadata;
    public string Id { get; } = id;
    public string Role { get; } = role;
    public string? TurnId { get; private set; }
    public IReadOnlyList<ComposerReference> InputReferences { get; internal set; } = [];
    public bool IsSteered => _steered == true;
    public bool CanEdit => Role == "You" && _steered == false && TurnId is not null;
    public string EditHint => _steered == true ? "Messages sent with Steer cannot be edited."
        : CanEdit ? "Edit this message and restart its reply. Later conversation history will be removed."
        : "Message details are unavailable. Refresh chat to load them.";
    internal void SetTurnInput(string turnId, bool? steered)
    {
        TurnId = turnId; _steered = steered;
        Changed(nameof(TurnId), nameof(IsSteered), nameof(CanEdit), nameof(EditHint));
    }
    public string Text { get => _text; set { _text = Wire.Short(value, 128 * 1024); _parsed = null; _questions = null; Changed(nameof(Text), nameof(CopyText), nameof(Preview), nameof(StatusTone), nameof(DisplayText), nameof(Annotations), nameof(Attachments), nameof(QuestionBlock), nameof(Questions), nameof(HasQuestions), nameof(CanAnswer), nameof(QuestionStatus)); } }
    private ParsedMessage Parsed => _parsed ??= Role == "You" ? MessagePayload.Parse(_text) : new(_text, [], []);
    public string DisplayText => Role == "Questions" || HasQuestions ? "" : Parsed.Body;
    public QuestionTranscript? QuestionBlock => _questionMetadata ?? (_questions ??= Role == "Questions" ? QuestionTranscript.Parse(_text) : Role == "You" ? QuestionTranscript.FromUserReply(_text) : null);
    public void SetQuestions(QuestionTranscript questions)
    {
        _questionMetadata = questions;
        Changed(nameof(QuestionBlock), nameof(Questions), nameof(HasQuestions), nameof(CanAnswer), nameof(QuestionStatus), nameof(DisplayRole), nameof(DisplayText), nameof(CopyText));
    }
    public IReadOnlyList<QuestionReply> Questions => QuestionBlock?.Questions ?? [];
    public bool HasQuestions => Questions.Count > 0;
    public bool CanAnswer => QuestionBlock?.CanAnswer == true;
    public string QuestionStatus => QuestionBlock is { CanAnswer: true, IsAsync: false, Questions.Count: > 1 } ? "Answer each question to send" : QuestionBlock?.Status ?? "";
    public string CopyText => HasQuestions ? string.Join("\n\n", Questions.Select(question => question.Question + (question.HasAnswer ? "\n" + question.Answer : ""))) : Text;
    public IReadOnlyList<ChatAnnotation> Annotations => Parsed.Annotations;
    public IReadOnlyList<ChatAttachment> Attachments => Parsed.Attachments;
    public bool TerminalExpanded { get => _terminalExpanded; set { if (_terminalExpanded == value) return; _terminalExpanded = value; Changed(); } }
    public bool IsStreaming { get => _streaming; set { if (_streaming == value) return; _streaming = value; Changed(); } }
    public string DisplayRole => HasQuestions && Role != "You" ? "Codex questions" : Role switch { "Files" => "Changed files", "Command" => "Terminal", _ => Role };
    public string Preview
    {
        get { var end = _text.IndexOf('\n'); return Wire.Short((end >= 0 ? _text[..end] : _text).Trim(), 100); }
    }
    public string StatusTone => Role != "Status" ? "Neutral" : _text.StartsWith("Task completed", StringComparison.OrdinalIgnoreCase) ? "Success"
        : new[] { "failed", "error", "lost", "unknown" }.Any(word => _text.Contains(word, StringComparison.OrdinalIgnoreCase)) ? "Warning" : "Neutral";
}

public enum ConversationActivityKind { Command, FileChange, WebSearch, Tool, Response }

public sealed class ConversationState(Guid localId)
{
    private readonly Dictionary<string, (ConversationActivityKind Kind, long Order)> _activities = [];
    private long _activityOrder;
    public Guid LocalId { get; } = localId;
    public string? ThreadId { get; set; }
    public string? TurnId { get; set; }
    public bool Submitting { get; set; }
    public DateTimeOffset? TaskStartedAt { get; set; }
    public DateTimeOffset? TaskFinishedAt { get; set; }
    public ConversationActivityKind? Activity { get; private set; }
    public Guid TaskRunId { get; set; }
    public string ActiveDirectory { get; set; } = "";
    public HashSet<string> EditedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public FileConflictPause? ConflictPause { get; set; }
    public bool PausingForConflict { get; set; }
    public bool ContinuingConflict { get; set; }
    public bool ConflictBlocked { get; set; }
    public string? LastTurnStatus { get; set; }
    public bool IsConflictPaused => ConflictPause is not null && !Busy && !Uncertain && !PausingForConflict && !ContinuingConflict;
    public string ConflictNotice => ConflictPause is not { } pause ? ""
        : Uncertain ? "File conflict: stop is unconfirmed. Reconnect and Refresh chat before continuing."
        : Busy || PausingForConflict ? "Stopping task after a file conflict…"
        : ContinuingConflict ? "Continuing task…"
        : ConflictBlocked ? "Paused: another task is editing " + string.Join(", ", pause.Files.Select(Path.GetFileName).Distinct())
        : "Paused after a file conflict. Ready to continue.";
    public string ConflictTooltip => ConflictPause is { } pause ? string.Join("\n", pause.Files) + "\n\nOther tasks:\n" + string.Join("\n", pause.Blockers.Select(blocker => blocker.Name).Distinct())
        + "\n\nStop does not undo file changes. Continue rereads the affected files in a new turn." : "";
    public bool Uncertain { get; set; }
    public bool Loaded { get; set; }
    public bool Loading { get; set; }
    public IReadOnlyList<string> ActiveFolders { get; set; } = [];
    public bool HasLargeTranscript => Messages.Count > 500 || Messages.Sum(item => item.Text.Length) > 2 * 1024 * 1024;
    public string? HistoryCursor { get; set; }
    public bool UsesTurnHistory { get; set; }
    internal Dictionary<string, string> FirstUserMessageIds { get; } = [];
    public long? ContextUsed { get; set; }
    public long? ContextCapacity { get; set; }
    public bool Compacting { get; set; }
    public string? ContextCompactionItemId { get; set; }
    public bool IsCompacting => Compacting || ContextCompactionItemId is not null;
    public bool Reviewing { get; set; }
    public bool Steering { get; set; }
    public ObservableCollection<QueuedMessage> Queue { get; set; } = [];
    public bool QueuePaused { get; set; }
    public bool QueueDispatching { get; set; }
    public bool QueueEditing { get; set; }
    public string QueueNotice { get; set; } = "";
    public string ContextUsedText => Tokens(ContextUsed);
    public string ContextCapacityText => Tokens(ContextCapacity);
    public string Context => $"Context used {ContextUsedText} / {ContextCapacityText}";
    public string ContextTooltip => ContextUsed is { } used && ContextCapacity is { } capacity && capacity > 0
        ? $"{used:N0} / {capacity:N0} tokens used ({Math.Clamp(used * 100.0 / capacity, 0, 100):0.#}%). Latest context usage reported by Codex, not cumulative billing usage."
        : "Used tokens / total context capacity. Waiting for Codex to report context usage.";
    private static string Tokens(long? value) => value is not { } count ? "?" : count >= 1_000_000 ? $"{count / 1_000_000.0:0.#}m" : count >= 1_000 ? $"{count / 1_000.0:0.#}k" : count.ToString("N0");
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public ObservableCollection<TranscriptEntry> Transcript { get; } = [];
    public TaskCompletionSource Terminal { get; set; } = Completed();
    public bool Busy => Submitting || !Terminal.Task.IsCompleted;
    public string Status => Uncertain ? "Outcome unknown" : ConflictPause is not null ? Busy || PausingForConflict ? "Stopping for conflict" : "Paused"
        : IsCompacting && Busy ? "Compacting" : Reviewing && Busy ? "Reviewing" : Busy ? "Running" : "Ready";

    private static TaskCompletionSource Completed() { var value = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); value.SetResult(); return value; }
    internal void BeginActivity(string? itemId, ConversationActivityKind kind)
    {
        if (itemId is null || _activities.TryGetValue(itemId, out var existing) && existing.Kind == kind) return;
        _activities[itemId] = (kind, ++_activityOrder);
        RefreshActivity();
    }
    internal void EndActivity(string? itemId)
    {
        if (itemId is not null && _activities.Remove(itemId)) RefreshActivity();
    }
    internal void ClearActivities() { _activities.Clear(); Activity = null; _activityOrder = 0; }
    private void RefreshActivity() => Activity = _activities.Values.OrderBy(activity => activity.Kind == ConversationActivityKind.Response)
        .ThenByDescending(activity => activity.Order).Select(activity => (ConversationActivityKind?)activity.Kind).FirstOrDefault();

    public void Put(string id, string role, string text, bool delta = false, bool prepend = false, bool streaming = false)
    {
        var message = Messages.FirstOrDefault(item => item.Id == id);
        if (message is not null) { if (streaming) message.IsStreaming = true; message.Text = delta ? message.Text + text : text; if (!streaming) message.IsStreaming = false; }
        else if (prepend) Messages.Insert(0, new ChatMessage(id, role, text));
        else Messages.Add(new ChatMessage(id, role, text) { IsStreaming = streaming });
        if (message is null) RefreshTranscript();
        else if (Transcript.FirstOrDefault(candidate => candidate.Items.Contains(message)) is { } entry) entry.Update(entry.Items.ToArray());
    }
    public void RefreshTranscript()
    {
        var existing = Transcript.ToDictionary(entry => entry.Key);
        var groups = new List<List<ChatMessage>>();
        foreach (var item in Messages)
            if ((item.Role is "Command" or "Files" or "Search") && (groups.LastOrDefault()?.LastOrDefault()?.Role is "Command" or "Files" or "Search")) groups[^1].Add(item);
            else groups.Add([item]);
        var desired = groups.Select(items =>
        {
            var entry = existing.GetValueOrDefault(items[0].Id);
            if (entry is null)
            {
                var previous = Transcript.FirstOrDefault(candidate => candidate.IsActivity && candidate.Items.Any(items.Contains));
                entry = new TranscriptEntry(items[0]);
                if (previous is not null) entry.Expanded = previous.Expanded;
            }
            entry.Update(items); return entry;
        }).ToArray();
        for (var index = Transcript.Count - 1; index >= 0; index--) if (!desired.Contains(Transcript[index])) Transcript.RemoveAt(index);
        for (var index = 0; index < desired.Length; index++)
            if (index >= Transcript.Count || !ReferenceEquals(Transcript[index], desired[index])) { var old = Transcript.IndexOf(desired[index]); if (old >= 0) Transcript.Move(old, index); else Transcript.Insert(index, desired[index]); }
    }
    public void Info(string text) => Put(Guid.NewGuid().ToString("N"), "Status", text);
    public void ReconcileQuestionReplies()
    {
        var answers = Messages.Where(message => message.Role == "You").SelectMany(message => message.Questions)
            .Where(question => question.HasAnswer).GroupBy(question => question.Id).ToDictionary(group => group.Key, group => group.Last().Answer);
        var lastOrdinaryUser = Messages.ToList().FindLastIndex(message => message.Role == "You" && !message.HasQuestions);
        for (var index = 0; index < Messages.Count; index++)
        {
            var message = Messages[index];
            if (message.Role == "You" || message.QuestionBlock is not { IsAsync: true } block) continue;
            var questions = block.Questions.Select(question => !question.HasAnswer && (answers.GetValueOrDefault(question.Id) ?? answers.GetValueOrDefault(message.Id)) is { } answer ? question.WithAnswer(answer) : question).ToArray();
            var status = questions.All(question => question.HasAnswer) ? "Reply sent" : index < lastOrdinaryUser ? "Question closed" : block.Status;
            if (!block.Questions.SequenceEqual(questions) || status != block.Status)
                message.SetQuestions(block with { Questions = questions, Status = status, RequestKey = status == "Awaiting your reply" ? block.RequestKey : null });
        }
    }
}

public sealed record CodexModel(string Id, string Name, IReadOnlyList<string> Efforts, string DefaultEffort, bool IsDefault);
public sealed record PendingApproval(CodexClient Connection, JsonElement Id, string Method, JsonElement Parameters)
{
    public string Key => Id.ToString();
    public string? ThreadId => Parameters.Text("threadId");
    public string? TurnId => Parameters.Text("turnId");
    public string Title => Method.Contains("fileChange", StringComparison.Ordinal) ? "Allow file changes?" : Method.Contains("permissions", StringComparison.Ordinal) ? "Grant requested permissions?" : Method.Contains("requestUserInput", StringComparison.Ordinal) ? "Codex needs your input" : "Allow this command?";
}
