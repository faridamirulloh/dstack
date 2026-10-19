using System.Windows;
using Microsoft.Win32;
using MiniCodex.Presentation;

namespace MiniCodex.Views;

public partial class EditAccountWindow : Window
{
    public string DisplayName => AccountName.Text.Trim();
    public string CodexHome => Home.Text.Trim();
    public EditAccountWindow(string name, string home)
    {
        InitializeComponent(); AccountName.Text = name; Home.Text = home;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }
    private void Browse(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose Codex home" };
        if (Directory.Exists(CodexHome)) picker.InitialDirectory = CodexHome;
        if (picker.ShowDialog(this) == true) Home.Text = picker.FolderName;
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (DisplayName.Length == 0) { Error.Text = "Enter an account name."; return; }
        if (!Path.IsPathFullyQualified(CodexHome)) { Error.Text = "Choose an absolute Codex home folder."; return; }
        DialogResult = true;
    }
}
