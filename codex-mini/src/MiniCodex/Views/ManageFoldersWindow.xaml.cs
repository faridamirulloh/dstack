using System.Windows;
using Microsoft.Win32;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.ViewModels;

namespace MiniCodex.Views;

public partial class ManageFoldersWindow : Window
{
    private readonly ManageFoldersViewModel _model;
    private readonly AppController _controller;
    private readonly Guid _profileId;
    private readonly Guid? _projectId;
    public Guid SavedProjectId { get; private set; }

    public ManageFoldersWindow(AppController controller, Guid profileId, ProjectState? project)
    {
        _controller = controller; _profileId = profileId; _projectId = project?.Id;
        _model = new ManageFoldersViewModel(project);
        InitializeComponent(); DataContext = _model;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private void AddFolders(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add project folders", Multiselect = true };
        if (_model.PrimaryFolder is { } primary && Directory.Exists(primary.Path)) dialog.InitialDirectory = primary.Path;
        if (dialog.ShowDialog(this) == true) _model.AddFolders(dialog.FolderNames);
    }

    private void RemoveFolder(object sender, RoutedEventArgs e) => _model.RemoveSelected();

    private void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            SavedProjectId = _controller.SaveProject(_profileId, _projectId, _model.CreatePlan());
            DialogResult = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _model.ShowError(error.Message); }
    }
}
