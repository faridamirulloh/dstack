using System.Text.Json;
using System.Text.Json.Serialization;
using MiniCodex.Codex;
using MiniCodex.Presentation;

namespace MiniCodex.Models;

public sealed class QuestionChoice(string label, string description) : ObservableObject
{
    private bool _selected;
    public string Label { get; } = label;
    public string Description { get; } = description;
    [JsonIgnore] public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(); } }
}
public sealed class QuestionReply(string id, string header, string question, IReadOnlyList<QuestionChoice> options, string answer) : ObservableObject
{
    private string _customAnswer = "";
    public string Id { get; } = id;
    public string Header { get; } = header;
    public string Question { get; } = question;
    public IReadOnlyList<QuestionChoice> Options { get; } = options;
    public string Answer { get; } = answer;
    public bool HasAnswer => Answer.Length > 0;
    [JsonIgnore] public string CustomAnswer
    {
        get => _customAnswer;
        set
        {
            if (_customAnswer == value) return;
            _customAnswer = value;
            if (value.Length > 0) foreach (var option in Options) option.IsSelected = false;
            Changed(nameof(CustomAnswer), nameof(SelectedAnswer));
        }
    }
    [JsonIgnore] public string? SelectedAnswer => !string.IsNullOrWhiteSpace(CustomAnswer) ? CustomAnswer.Trim() : Options.FirstOrDefault(option => option.IsSelected)?.Label;
    public void SelectAnswer(QuestionChoice selected)
    {
        CustomAnswer = "";
        foreach (var option in Options) option.IsSelected = ReferenceEquals(option, selected);
        Changed(nameof(SelectedAnswer));
    }
    public QuestionReply WithAnswer(string value) => new(Id, Header, Question, Options, value);
}
public sealed record QuestionTranscript(string Status, string? RequestKey, IReadOnlyList<QuestionReply> Questions)
{
    public bool IsAsync { get; init; }
    public bool CanAnswer => Status == "Awaiting your reply" && RequestKey is not null;
    public static QuestionTranscript From(JsonElement questions, JsonElement answers, string status, string? requestKey = null) =>
        new(status, requestKey, questions.Items().Take(3).Select(question =>
        {
            var id = question.Text("id") ?? "";
            var answer = string.Join("\n", answers.Get(id).Get("answers").Items().Select(item => item.Text()).OfType<string>());
            return new QuestionReply(id, question.Text("header") ?? "Question", question.Text("question") ?? "",
                question.Get("options").Items().Take(20).Select(option => new QuestionChoice(option.Text("label") ?? "", option.Text("description") ?? "")).ToArray(), answer);
        }).ToArray());
    public static QuestionTranscript FromAsync(JsonElement questions, string messageId) => new("Awaiting your reply", "async/" + messageId,
        questions.Items().Select((question, index) => new QuestionReply(
            JsonSerializer.Serialize(new object[] { "request_user_input_async", messageId, index }), "Question", question.Text("title") ?? "",
            question.Get("options").Items().Select(option => new QuestionChoice(option.Text() ?? "", "")).Where(option => option.Label.Length > 0).ToArray(), "")).ToArray()) { IsAsync = true };
    public static string ReplyMessage(QuestionReply question, string answer) => "<send_user_message_question_reply>\n"
        + JsonSerializer.Serialize(new[] { new { questionItemId = question.Id, question = question.Question, answer } })
        + "\n</send_user_message_question_reply>";
    public static QuestionTranscript? Parse(string text)
    {
        try { return JsonSerializer.Deserialize<QuestionTranscript>(text); }
        catch (JsonException) { return null; }
    }
    public string Serialize() => JsonSerializer.Serialize(this);
    public static QuestionTranscript? FromUserReply(string text)
    {
        const string start = "<send_user_message_question_reply>";
        const string end = "</send_user_message_question_reply>";
        text = text.Trim();
        if (!text.StartsWith(start, StringComparison.Ordinal) || !text.EndsWith(end, StringComparison.Ordinal)) return null;
        try
        {
            using var document = JsonDocument.Parse(text[start.Length..^end.Length].Trim());
            var items = document.RootElement.ValueKind == JsonValueKind.Object ? new[] { document.RootElement } : document.RootElement.Items();
            var replies = items.Select(item =>
            {
                var answer = item.Text("answer") ?? string.Join("\n", item.Get("answer").Items().Select(value => value.Text()).OfType<string>());
                return new QuestionReply(item.Text("questionItemId") ?? "", "Question", item.Text("question") ?? "", [], answer);
            }).Where(reply => reply.Question.Length > 0).ToArray();
            return replies.Length > 0 ? new("Reply sent", null, replies) { IsAsync = true } : null;
        }
        catch (JsonException) { return null; }
    }
    public static JsonElement OutputAnswers(JsonElement content)
    {
        foreach (var item in content.Items())
        {
            var text = item.Text("text");
            if (text is null) continue;
            try
            {
                using var document = JsonDocument.Parse(text);
                var answers = document.RootElement.Get("answers");
                if (answers.ValueKind == JsonValueKind.Object) return answers.Clone();
            }
            catch (JsonException) { }
        }
        return default;
    }
}
