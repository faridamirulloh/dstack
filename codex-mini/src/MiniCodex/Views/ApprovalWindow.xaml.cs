using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MiniCodex.Codex;
using MiniCodex.Presentation;

namespace MiniCodex.Views;

public partial class ApprovalWindow : Window
{
    private readonly Dictionary<string, TextBox> _answers = [];
    private readonly bool _questions;
    public bool ForSession { get; private set; }
    public Dictionary<string, object> Answers => _answers.ToDictionary(pair => pair.Key, pair => (object)new { answers = new[] { pair.Value.Text } });

    public ApprovalWindow(PendingApproval request, string profileName, IReadOnlyDictionary<string, string>? selectedAnswers = null)
    {
        InitializeComponent(); Heading.Text = request.Title;
        Scope.Text = profileName + " · Chat " + request.ThreadId;
        _questions = request.Method.Contains("requestUserInput", StringComparison.Ordinal);
        var parameters = request.Parameters;
        var details = JsonSerializer.Serialize(parameters, new JsonSerializerOptions { WriteIndented = true });
        Details.Text = Wire.Short(details, 64 * 1024);
        AllowSession.Visibility = request.Method.Contains("commandExecution", StringComparison.Ordinal) || request.Method.Contains("fileChange", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        var available = parameters.Get("availableDecisions").Items().Select(item => item.Text()).OfType<string>().ToArray();
        if (available.Length > 0)
        {
            AllowSession.IsEnabled = available.Contains("acceptForSession");
            AllowOnce.IsEnabled = _questions || request.Method.Contains("permissions", StringComparison.Ordinal) || available.Contains("accept");
        }
        if (details.Length > 64 * 1024)
        { AllowSession.IsEnabled = false; AllowOnce.IsEnabled = false; Scope.Text += "\nRequest exceeds the review display limit. Decline it and ask Codex for a smaller action."; }
        if (_questions)
        {
            Title = "Reply to Codex"; Heading.Text = "Codex has a question";
            Details.Visibility = Visibility.Collapsed; AllowSession.Visibility = Visibility.Collapsed; AllowOnce.Content = "Submit answers";
            var number = 0;
            foreach (var question in parameters.Get("questions").Items().Take(3))
            {
                var id = question.Text("id"); if (id is null) continue;
                var panel = new StackPanel();
                var card = new Border { Child = panel, Padding = new Thickness(16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
                card.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                panel.Children.Add(new TextBlock { Text = $"{++number}  ·  " + (question.Text("header") ?? "Question"), FontSize = 11, Foreground = (Brush)FindResource("AccentBrush"), Margin = new Thickness(0, 0, 0, 8) });
                panel.Children.Add(new TextBlock { Text = question.Text("question") ?? id, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
                var input = new TextBox { Margin = new Thickness(0, 7, 0, 0), MinHeight = 38, MaxHeight = 140, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 16000 };
                var choices = new List<ToggleButton>();
                foreach (var option in question.Get("options").Items().Take(20))
                {
                    var label = option.Text("label") ?? "";
                    var content = new StackPanel();
                    content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
                    if (option.Text("description") is { Length: > 0 } description)
                        content.Children.Add(new TextBlock { Text = description, Foreground = (Brush)FindResource("MutedBrush"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
                    var button = new ToggleButton { Content = content, Tag = label, Style = (Style)FindResource("QuestionChoiceButton") };
                    button.Click += (_, _) =>
                    {
                        input.Text = label;
                        foreach (var choice in choices) choice.IsChecked = choice.Tag as string == label;
                        if (_answers.Count > 0 && _answers.Values.All(answer => !string.IsNullOrWhiteSpace(answer.Text))) DialogResult = true;
                    };
                    choices.Add(button); panel.Children.Add(button);
                }
                input.TextChanged += (_, _) => { foreach (var choice in choices) choice.IsChecked = input.Text == (string)choice.Tag; };
                panel.Children.Add(new TextBlock { Text = "Your answer or a custom response", FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 12, 0, 0) });
                _answers[id] = input; panel.Children.Add(input); Fields.Children.Add(card);
                if (selectedAnswers is not null && selectedAnswers.TryGetValue(id, out var selected)) input.Text = selected;
            }
        }
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }
    private void Accept(object sender, RoutedEventArgs e)
    {
        if (_questions && _answers.Values.Any(input => string.IsNullOrWhiteSpace(input.Text)))
        { AppDialog.Show(this, "Answer each question before submitting.", "Answers required"); return; }
        DialogResult = true;
    }
    private void AcceptSession(object sender, RoutedEventArgs e) { ForSession = true; DialogResult = true; }
}
