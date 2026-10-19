using System.Collections.ObjectModel;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.ViewModels;

public sealed class ManageFoldersViewModel : ObservableObject
{
    private string _name;
    private FolderOption? _selectedFolder;
    private FolderOption? _primaryFolder;
    private string _notice = "";
    public ObservableCollection<FolderOption> Folders { get; } = [];
    public string Name { get => _name; set { _name = value; Changed(); } }
    public string Notice { get => _notice; private set { _notice = value; Changed(); } }
    public FolderOption? SelectedFolder { get => _selectedFolder; set { _selectedFolder = value; Changed(); Changed(nameof(CanRemove)); } }
    public FolderOption? PrimaryFolder { get => _primaryFolder; set { _primaryFolder = value; Changed(); Notice = ""; } }
    public bool CanRemove => SelectedFolder is not null && Folders.Count > 1;

    public ManageFoldersViewModel(ProjectState? project)
    {
        _name = project?.Name ?? "New project";
        foreach (var path in project?.Folders ?? []) Folders.Add(new FolderOption(path));
        _primaryFolder = Folders.FirstOrDefault(folder => FolderPaths.Comparer.Equals(folder.Path, project?.PrimaryFolder));
        _selectedFolder = Folders.FirstOrDefault();
    }

    public void AddFolders(IEnumerable<string> paths)
    {
        var skipped = 0;
        var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var canonical = FolderPaths.Canonicalize(path);
                if (Folders.Any(folder => FolderPaths.Comparer.Equals(folder.Path, canonical))) { skipped++; continue; }
                var option = new FolderOption(canonical);
                Folders.Add(option); SelectedFolder = option;
                if (PrimaryFolder is null) PrimaryFolder = option;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { errors.Add(error.Message); }
        }
        Changed(nameof(CanRemove));
        Notice = string.Join(Environment.NewLine, errors.Append(skipped > 0 ? $"Skipped {skipped} folder(s) already in this project." : "").Where(text => text.Length > 0));
    }

    public void RemoveSelected()
    {
        if (SelectedFolder is null) return;
        if (Folders.Count == 1) { Notice = "A project needs at least one folder."; return; }
        if (PrimaryFolder == SelectedFolder) { Notice = "Choose a different primary folder before removing this folder."; return; }
        Folders.Remove(SelectedFolder);
        SelectedFolder = Folders.FirstOrDefault();
        Notice = "Folder removed from this project. Its files have not been changed.";
        Changed(nameof(CanRemove));
    }

    public FolderPlan CreatePlan()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("Enter a project name.");
        if (Folders.Count == 0) throw new InvalidOperationException("Add at least one folder.");
        if (PrimaryFolder is null || !Folders.Contains(PrimaryFolder)) throw new InvalidOperationException("Choose a primary folder.");
        var canonical = Folders.Select(folder => FolderPaths.Canonicalize(folder.Path)).Distinct(FolderPaths.Comparer).ToArray();
        return new FolderPlan(Name.Trim(), canonical, FolderPaths.Canonicalize(PrimaryFolder.Path));
    }

    public void ShowError(string message) => Notice = message;
}
