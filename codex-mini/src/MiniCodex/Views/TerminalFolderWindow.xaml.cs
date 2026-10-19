using System.Windows;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.Views;

public partial class TerminalFolderWindow : Window
{
    public string? SelectedFolder => (Folders.SelectedItem as FolderOption)?.Path;
    public TerminalFolderWindow(ProjectState project)
    {
        InitializeComponent();
        ProjectName.Text = project.Name;
        var folders = project.Folders.Distinct(FolderPaths.Comparer).Select(path => new FolderOption(path)).ToArray();
        Folders.ItemsSource = folders;
        Folders.SelectedItem = folders.FirstOrDefault(folder => FolderPaths.Comparer.Equals(folder.Path, project.PrimaryFolder)) ?? folders.FirstOrDefault();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) => Folders.Focus();
    }
    private void Accept(object sender, RoutedEventArgs e)
    {
        if (SelectedFolder is not { } path) { Error.Text = "Choose a project folder."; return; }
        if (!Directory.Exists(path)) { Error.Text = "This folder is unavailable. Choose another folder or update the project folders."; return; }
        DialogResult = true;
    }
}
