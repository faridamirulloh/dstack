using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MiniCodex.Codex;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.Views;

public partial class CodexInstructionsWindow : Window
{
    private readonly AppController _controller;
    private readonly Guid _profileId;
    private readonly CodexInstructionsStore _store;
    private CodexInstructionsSnapshot? _snapshot;
    private bool _saved;
    public CodexInstructionsWindow(AppController controller, Guid profileId)
    {
        _controller = controller; _profileId = profileId;
        var profile = controller.Profile(profileId);
        _store = new CodexInstructionsStore(CodexInstallation.Home(profile));
        InitializeComponent(); Scope.Text = profile.Name + " · All projects in this account";
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Closing += ConfirmClose;
        LoadInstructions();
    }
    private bool HasEdits => _snapshot is not null && Input.Text != _snapshot.Text;
    private void LoadInstructions()
    {
        try
        {
            _snapshot = _store.Load(); Input.Text = _snapshot.Text;
            FileLocation.Text = _snapshot.FilePath; FileLocation.ToolTip = _snapshot.FilePath;
            OverrideHint.Visibility = _snapshot.IsOverride ? Visibility.Visible : Visibility.Collapsed;
            Input.IsEnabled = true; SaveButton.IsEnabled = true; Status.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _snapshot = null; Input.IsEnabled = false; SaveButton.IsEnabled = false; Status.Text = error.Message; }
    }
    private bool ConfirmDiscard() => !HasEdits || AppDialog.Show(this, "Discard your unsaved instruction edits?", "Unsaved instructions", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK;
    private void ConfirmClose(object? sender, CancelEventArgs e) { if (!_saved && !ConfirmDiscard()) e.Cancel = true; }
    private void Reload(object sender, RoutedEventArgs e) { if (ConfirmDiscard()) LoadInstructions(); }
    private void InstructionsChanged(object sender, TextChangedEventArgs e) { if (Status is not null) Status.Text = ""; }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null) return;
        try
        {
            if (!FolderPaths.Comparer.Equals(Path.GetFullPath(CodexInstallation.Home(_controller.Profile(_profileId))), _store.HomePath))
                throw new IOException("This account's Codex home changed. Reopen Codex instructions before saving.");
            _store.Save(_snapshot, Input.Text); _saved = true; DialogResult = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Status.Text = error.Message; }
    }
}
