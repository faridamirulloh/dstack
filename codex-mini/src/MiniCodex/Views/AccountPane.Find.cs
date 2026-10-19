using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MiniCodex.Codex;
using MiniCodex.Models;
using MiniCodex.Presentation;

namespace MiniCodex.Views;

public partial class AccountPane
{
    private readonly DispatcherTimer _findTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly List<(string Id, string Part, int Index, int Occurrence)> _findMatches = [];
    private int _findIndex = -1;
    private void InitializeFind()
    {
        _findTimer.Tick += FindTick; Runtime.Updated += FindUpdated; _model.PropertyChanged += FindModelChanged;
        PreviewKeyDown += FindShortcut;
    }
    private void DisposeFind() { _findTimer.Stop(); _findTimer.Tick -= FindTick; Runtime.Updated -= FindUpdated; _model.PropertyChanged -= FindModelChanged; PreviewKeyDown -= FindShortcut; }
    private void FindModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(ViewModels.AccountViewModel.SelectedChat)) { _findIndex = -1; FindUpdated(sender, EventArgs.Empty); } }
    private void FindUpdated(object? sender, EventArgs e) { if (FindBar.Visibility == Visibility.Visible && !_findTimer.IsEnabled) _findTimer.Start(); }
    private void FindTick(object? sender, EventArgs e) { _findTimer.Stop(); var changed = _model.FindQuery != FindInput.Text; RefreshFind(); if (changed) MoveFind(0); }
    private void FindShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; ShowFind(sender, e); }
        else if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible) { e.Handled = true; CloseFind(sender, e); }
    }
    private void ShowFind(object sender, RoutedEventArgs e)
    { Runtime.Touch(); FindBar.Visibility = Visibility.Visible; FindInput.Focus(); FindInput.SelectAll(); RefreshFind(); }
    private void CloseFind(object sender, RoutedEventArgs e)
    { FindBar.Visibility = Visibility.Collapsed; FindInput.Text = ""; _model.FindQuery = ""; _findTimer.Stop(); Composer.Focus(); }
    private void FindChanged(object sender, TextChangedEventArgs e) { if (!_findTimer.IsEnabled) _findTimer.Start(); }
    private void FindInputKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; MoveFind(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); } }
    private static IEnumerable<(string Part, int Index, string Text)> Findable(ChatMessage message)
    {
        for (var index = 0; index < message.Annotations.Count; index++)
        {
            yield return ("quote", index, message.Annotations[index].Quote);
            yield return ("comment", index, message.Annotations[index].Comment);
        }
        for (var index = 0; index < message.Questions.Count; index++)
        {
            var question = message.Questions[index];
            yield return ("question", index, question.Header + "\n" + question.Question + "\n" + (question.HasAnswer ? question.Answer : string.Join("\n", question.Options.Select(option => option.Label + "\n" + option.Description))));
        }
        if (!message.HasQuestions) yield return ("body", 0, message.DisplayText);
    }
    private void RefreshFind()
    {
        var query = FindInput.Text; _model.FindQuery = query;
        var current = _findIndex >= 0 && _findIndex < _findMatches.Count ? _findMatches[_findIndex] : default;
        _findMatches.Clear();
        if (query.Length > 0)
            foreach (var message in _model.Messages)
            {
                foreach (var region in Findable(message))
                {
                    var offset = 0; var occurrence = 0;
                    while (offset <= region.Text.Length - query.Length && _findMatches.Count < 5000)
                    {
                        var found = region.Text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase); if (found < 0) break;
                        _findMatches.Add((message.Id, region.Part, region.Index, occurrence++)); offset = found + query.Length;
                    }
                    if (_findMatches.Count >= 5000) break;
                }
                if (_findMatches.Count >= 5000) break;
            }
        _findIndex = _findMatches.FindIndex(match => match == current);
        if (_findIndex < 0 && _findMatches.Count > 0) _findIndex = 0;
        UpdateFindLabel();
    }
    private void UpdateFindLabel() => FindCount.Text = _findMatches.Count == 0 ? "No matches · loaded messages" : $"{_findIndex + 1} / {_findMatches.Count}{(_findMatches.Count == 5000 ? "+" : "")}";
    private void FindPrevious(object sender, RoutedEventArgs e) => MoveFind(-1);
    private void FindNext(object sender, RoutedEventArgs e) => MoveFind(1);
    private void MoveFind(int direction)
    {
        RefreshFind(); if (_findMatches.Count == 0) return;
        _findIndex = (_findIndex + direction + _findMatches.Count) % _findMatches.Count;
        UpdateFindLabel(); var match = _findMatches[_findIndex]; var query = FindInput.Text;
        var entry = _model.DisplayMessages.FirstOrDefault(entry => entry.Items.Any(message => message.Id == match.Id)); if (entry is null) return;
        CancelHistoryRestore(); _followTail = false;
        if (entry.IsActivity) entry.Expanded = true;
        Transcript.ScrollIntoView(entry);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (query != FindInput.Text || string.IsNullOrEmpty(query) || Transcript.ItemContainerGenerator.ContainerFromItem(entry) is not DependencyObject container) return;
            var message = entry.Items.FirstOrDefault(message => message.Id == match.Id); if (message is null) return;
            object? card = match.Part switch
            {
                "quote" or "comment" => message.Annotations.ElementAtOrDefault(match.Index),
                "question" => message.Questions.ElementAtOrDefault(match.Index),
                _ => null
            };
            if (card is not null)
            {
                var border = Descendants(container).OfType<Border>().FirstOrDefault(border => border.Tag is "FindAnnotation" or "FindQuestion" && ReferenceEquals(border.DataContext, card));
                if (border is not null)
                {
                    ChatMessageSurface.AllowNavigation(border);
                    border.BringIntoView();
                }
                if (match.Part != "comment") return;
            }
            foreach (var view in Descendants(container).OfType<MarkdownMessageView>().Where(view => view.IsVisible && (card is not null ? ReferenceEquals(view.DataContext, card) : view.DataContext is ChatMessage item && item.Id == message.Id || view.DataContext is TranscriptEntry row && row.Message.Id == message.Id)))
            {
                if (view.SelectFind(query, match.Occurrence)) return;
                if (view.SelectFind(query, 0)) return;
            }
        }));
    }
}
