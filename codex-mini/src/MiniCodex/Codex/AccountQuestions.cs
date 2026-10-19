using System.Text.Json;
using MiniCodex.Models;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    private readonly HashSet<(Guid Chat, string Question)> _answeringQuestions = [];
    public async Task AnswerQuestionAsync(ConversationState state, ChatMessage message, QuestionReply question, string answer)
    {
        if (!Connected || !SignedIn || !state.Messages.Contains(message) || message.QuestionBlock is not { CanAnswer: true } block
            || !block.Questions.Contains(question) || question.HasAnswer || string.IsNullOrWhiteSpace(answer)) return;
        if (!block.IsAsync)
        {
            var request = Approvals.FirstOrDefault(request => request.Key == block.RequestKey && request.ThreadId == state.ThreadId);
            if (request is null || _resolvingApprovals.Contains(request.Key) || block.Questions.Any(question => string.IsNullOrWhiteSpace(question.SelectedAnswer))) return;
            await ResolveApprovalAsync(request, true, false, block.Questions.ToDictionary(question => question.Id, question => (object)new { answers = new[] { question.SelectedAnswer! } }));
            return;
        }
        if (state.Uncertain || state.Loading || state.Submitting || state.IsCompacting || state.Steering || state.QueueDispatching)
            throw new InvalidOperationException("Wait for the current submission or refresh to finish before answering.");
        var project = controller.Profile(ProfileId).Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == state.LocalId))
            ?? throw new InvalidOperationException("This question's project is unavailable.");
        var chat = project.Chats.Single(chat => chat.Id == state.LocalId);
        if (chat.IsArchived) throw new InvalidOperationException("Restore this chat before answering.");
        if (!_answeringQuestions.Add((state.LocalId, question.Id))) return;
        message.SetQuestions(block with { Status = "Sending reply…" }); Notify();
        try
        {
            var text = QuestionTranscript.ReplyMessage(question, answer.Trim());
            if (state.Busy) await SteerAsync(project, chat, text, [], []);
            else
            {
                var profile = controller.Profile(ProfileId);
                var model = Models.FirstOrDefault(model => model.Id == profile.SelectedModel) ?? Models.FirstOrDefault(model => model.IsDefault) ?? Models.FirstOrDefault()
                    ?? throw new InvalidOperationException("Choose an available model before answering.");
                await SendAsync(project, chat, text, model, profile.Effort, profile.ApprovalMode, fastMode: profile.FastMode);
            }
            var questions = block.Questions.Select(item => ReferenceEquals(item, question) ? question.WithAnswer(answer.Trim()) : item).ToArray();
            var pending = questions.Any(item => !item.HasAnswer);
            message.SetQuestions(block with { Questions = questions, Status = pending ? "Awaiting your reply" : "Reply sent", RequestKey = pending ? block.RequestKey : null });
            state.ReconcileQuestionReplies();
        }
        catch
        {
            message.SetQuestions(state.Uncertain ? block with { Status = "Reply outcome unknown", RequestKey = null } : block);
            throw;
        }
        finally { _answeringQuestions.Remove((state.LocalId, question.Id)); Notify(); }
    }

    private static string QuestionMessageId(PendingApproval approval) => "questions/" + (approval.Parameters.Text("itemId") ?? approval.Key);
    private void RecordQuestions(PendingApproval approval, string status, Dictionary<string, object>? answers = null)
    {
        if (!approval.Method.Contains("requestUserInput", StringComparison.Ordinal) || Find(approval.ThreadId) is not { } state) return;
        var data = answers is null ? default : JsonSerializer.SerializeToElement(answers);
        var transcript = QuestionTranscript.From(approval.Parameters.Get("questions"), data, status,
            status == "Awaiting your reply" ? approval.Key : null);
        state.Put(QuestionMessageId(approval), "Questions", transcript.Serialize());
    }
    private void CloseQuestions(PendingApproval approval)
    {
        if (Find(approval.ThreadId) is not { } state) return;
        var message = state.Messages.FirstOrDefault(item => item.Id == QuestionMessageId(approval));
        if (message?.QuestionBlock is { CanAnswer: true } block)
            state.Put(message.Id, "Questions", (block with { Status = "Question closed", RequestKey = null }).Serialize());
    }
}
