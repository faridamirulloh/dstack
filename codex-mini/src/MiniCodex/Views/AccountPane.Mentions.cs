using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.ViewModels;

namespace MiniCodex.Views;

public partial class AccountPane
{
    private DispatcherTimer? _mentionTimer;
    private CancellationTokenSource? _mentionSearch;
    private ProjectExplorerWindow? _explorerWindow;
    private int _mentionSequence;
    private bool _insertingReference;

    private void InitializeMentions()
    {
        _mentionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _mentionTimer.Tick += FindMentions;
        _model.PropertyChanged += MentionContextChanged;
    }
    private void DisposeMentions()
    {
        CloseMentions();
        _mentionSearch?.Dispose(); _mentionSearch = null;
        if (_mentionTimer is not null) _mentionTimer.Tick -= FindMentions;
        _model.PropertyChanged -= MentionContextChanged;
    }
    private void MentionContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountViewModel.SelectedChat) or nameof(AccountViewModel.SelectedProject)) CloseMentions();
    }
    private void OpenFileExplorer(object sender, RoutedEventArgs e) => OpenExplorer(true);
    private void OpenCapabilityExplorer(object sender, RoutedEventArgs e) => OpenExplorer(false);
    private void OpenExplorer(bool files)
    {
        if (!_model.HasProject) return;
        CloseMentions();
        if (_explorerWindow is not null) { _explorerWindow.SetTab(files); DialogPlacement.CenterOn(_explorerWindow, DialogAnchor); _explorerWindow.Activate(); return; }
        _explorerWindow = PrepareDialog(new ProjectExplorerWindow(_model.Explorer, reference =>
        {
            InsertReference(reference, false);
            _explorerWindow?.Close();
            HostWindow.Activate(); Composer.Focus();
        }, files));
        _explorerWindow.Closed += (_, _) => _explorerWindow = null;
        _explorerWindow.Show();
    }
    private void ComposerTextChanged(object sender, TextChangedEventArgs e) { if (!Composer.IsUpdating) QueueMentions(); }
    private void ComposerSelectionChanged(object sender, RoutedEventArgs e) => QueueMentions();
    private void QueueMentions()
    {
        if (_mentionTimer is null || _insertingReference || Composer.IsUpdating) return;
        _mentionSequence++; _mentionSearch?.Cancel(); _mentionTimer.Stop();
        if (TryMentionQuery(out _, out _, out _)) _mentionTimer.Start();
        else MentionPopup.IsOpen = false;
    }
    private bool TryMentionQuery(out int start, out char prefix, out string query)
    {
        start = Composer.CaretIndex; prefix = default; query = "";
        if (!_model.HasChat || Composer.SelectionLength > 0 || !Composer.IsKeyboardFocusWithin) return false;
        var text = Composer.Text;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        if (start >= Composer.CaretIndex || text[start] is not ('@' or '/')) return false;
        prefix = text[start]; query = text[(start + 1)..Composer.CaretIndex];
        return query.Length <= 160 && !query.Contains(']') && !query.Contains('(');
    }
    private async void FindMentions(object? sender, EventArgs e)
    {
        _mentionTimer?.Stop();
        if (!TryMentionQuery(out _, out var prefix, out var query)) return;
        var sequence = _mentionSequence;
        var chat = _model.SelectedChat?.Id;
        var project = _model.SelectedProject?.Id;
        _mentionSearch?.Dispose();
        var cancellation = new CancellationTokenSource(); _mentionSearch = cancellation;
        var token = cancellation.Token;
        MentionList.ItemsSource = null;
        MentionNotice.Text = "Finding mentions…"; MentionPopup.IsOpen = true;
        try
        {
            var capabilities = _model.Explorer.EnsureCapabilitiesAsync();
            IReadOnlyList<ComposerReference> fileRows = [];
            var fileNotice = "";
            bool Publish(bool loading)
            {
                if (_closing || sequence != _mentionSequence || token.IsCancellationRequested || chat != _model.SelectedChat?.Id || project != _model.SelectedProject?.Id || !Composer.IsKeyboardFocusWithin) return false;
                var rows = _model.Explorer.Entries.Where(entry => entry.Enabled && (prefix != '/' || entry.Kind == ReferenceKind.Skill)
                    && (entry.Name + " " + entry.Source + " " + entry.Description).Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20).Concat(fileRows);
                var sorted = rows.OrderBy(row => row.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(row => row.Kind).Take(40).ToArray();
                var previous = MentionList.SelectedItem as ComposerReference;
                MentionList.ItemsSource = sorted;
                MentionList.SelectedItem = sorted.FirstOrDefault(row => previous is not null && row.Kind == previous.Kind && row.Path == previous.Path) ?? sorted.FirstOrDefault();
                MentionNotice.Text = sorted.Length == 0 ? loading ? "Loading skills and plugins…" : "No matches. Open an explorer to browse or refresh capabilities."
                    : "↑ ↓ to select · Enter or Tab to insert · Esc to close" + (loading ? "\nSkills and plugins are still loading." : "") + (fileNotice.Length > 0 ? "\n" + fileNotice : "");
                MentionPopup.IsOpen = true;
                return true;
            }
            if (prefix == '@')
            {
                var roots = _model.Explorer.Folders;
                var files = await Task.Run(() => ProjectFiles.Find(roots, query, token), token);
                fileRows = files.Entries; fileNotice = files.Notice;
                if (!Publish(!capabilities.IsCompleted)) return;
            }
            await capabilities;
            Publish(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (sequence == _mentionSequence && !_closing) MentionNotice.Text = error.Message;
        }
    }
    private void CloseMentions()
    {
        _mentionSequence++; _mentionTimer?.Stop(); _mentionSearch?.Cancel();
        MentionPopup.IsOpen = false;
    }
    private void MentionPopupClosed(object? sender, EventArgs e)
    { _mentionSequence++; _mentionTimer?.Stop(); _mentionSearch?.Cancel(); }
    private bool HandleMentionKey(KeyEventArgs e)
    {
        if (!MentionPopup.IsOpen)
        {
            if (_mentionTimer?.IsEnabled == true && (e.Key is Key.Enter or Key.Tab) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && TryMentionQuery(out _, out _, out _))
            { FindMentions(null, EventArgs.Empty); e.Handled = true; return true; }
            return false;
        }
        if (e.Key == Key.Escape) { CloseMentions(); e.Handled = true; return true; }
        if (e.Key is Key.Down or Key.Up)
        {
            if (MentionList.Items.Count > 0)
            {
                MentionList.SelectedIndex = Math.Clamp(MentionList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, MentionList.Items.Count - 1);
                MentionList.ScrollIntoView(MentionList.SelectedItem);
            }
            e.Handled = true; return true;
        }
        if ((e.Key is Key.Enter or Key.Tab) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (MentionList.SelectedItem is ComposerReference reference) Guard(() => InsertReference(reference, true)); else CloseMentions();
            e.Handled = true; return true;
        }
        return false;
    }
    private void MentionKeyDown(object sender, KeyEventArgs e) => HandleMentionKey(e);
    private void MentionClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(MentionList, source) is ListBoxItem { Content: ComposerReference reference })
        { Guard(() => InsertReference(reference, true)); e.Handled = true; }
    }
    private void InsertReference(ComposerReference reference, bool replaceQuery)
    {
        if (!_model.Explorer.CanInsert || !reference.Enabled) return;
        if (reference.Kind is ReferenceKind.File or ReferenceKind.Folder) ProjectFiles.Validate(reference.Path, _model.Explorer.Folders);
        else if (!_model.Explorer.Entries.Any(entry => entry.Enabled && entry.Kind == reference.Kind && entry.Path == reference.Path))
            throw new InvalidOperationException("This capability changed. Refresh the explorer and select it again.");
        var start = Composer.SelectionStart;
        var length = Composer.SelectionLength;
        if (replaceQuery)
        {
            // The popup can own keyboard focus after a mouse click.
            var text = Composer.Text;
            start = Composer.CaretIndex;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
            if (start >= text.Length || text[start] is not ('@' or '/')) return;
            length = Composer.CaretIndex - start;
        }
        CloseMentions(); _insertingReference = true;
        try
        {
            Composer.InsertReference(reference, start, length);
            _model.Draft = Composer.Text;
            _model.TrackReference(reference);
            Composer.Focus();
        }
        finally { _insertingReference = false; }
    }
}
