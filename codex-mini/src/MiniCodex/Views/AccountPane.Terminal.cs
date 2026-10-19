using System.Windows.Input;
using MiniCodex.Services;

namespace MiniCodex.Views;

public partial class AccountPane
{
    private void TerminalShortcut(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Oem3 || Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        if (!e.IsRepeat) Guard(OpenProjectTerminal);
    }
    private void OpenProjectTerminal()
    {
        if (_model.SelectedProject is not { } project) throw new InvalidOperationException("Select a project before opening a terminal.");
        Runtime.Touch();
        var folders = project.Folders.Distinct(FolderPaths.Comparer).ToArray();
        if (folders.Length == 0) throw new InvalidOperationException("Add a project folder before opening a terminal.");
        var folder = folders[0];
        if (folders.Length > 1)
        {
            var picker = PrepareDialog(new TerminalFolderWindow(project));
            if (picker.ShowDialog() != true || picker.SelectedFolder is not { } selected) return;
            folder = selected;
        }
        ProjectTerminal.Open(folder);
    }
}
