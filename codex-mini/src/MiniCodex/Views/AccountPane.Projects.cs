using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.ViewModels;

namespace MiniCodex.Views;

public partial class AccountPane
{
    private const string ProjectDragFormat = "MiniCodex.ProjectOrder";
    private Point _projectDragStart;
    private FrameworkElement? _projectDragHeader;
    private ProjectDropAdorner? _projectDropAdorner;

    private void UseLastActiveProjectOrder(object sender, RoutedEventArgs e) => Guard(_model.UseLastActiveProjectOrder);

    private void PrepareProjectFolderMenu(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (sender is not ContextMenu { DataContext: SessionTreeNode node } menu) return;
        var deleteArchived = menu.Items.OfType<MenuItem>().First(option => option.Tag as string == "DeleteArchived");
        deleteArchived.Visibility = _model.ShowArchived ? Visibility.Visible : Visibility.Collapsed;
        var folders = _model.FoldersForNode(node);
        foreach (var item in menu.Items.OfType<MenuItem>().Where(option => option.Tag is "ProjectFolders" or "ProjectTerminal"))
        {
            item.Items.Clear();
            item.IsEnabled = folders.Count > 0;
            item.CommandParameter = folders.Count == 1 ? folders[0] : null;
            item.ToolTip = item.CommandParameter;
            if (folders.Count < 2) continue;
            RoutedEventHandler openFolder = item.Tag is "ProjectTerminal" ? OpenProjectTerminalFolder : OpenProjectFolder;
            foreach (var folder in folders)
            {
                var option = new MenuItem
                {
                    Header = new TextBlock { Text = FolderPaths.Name(folder, folder), MaxWidth = 480, TextTrimming = TextTrimming.CharacterEllipsis },
                    ToolTip = folder, CommandParameter = folder
                };
                option.Click += openFolder;
                item.Items.Add(option);
            }
        }
    });

    private void OpenProjectFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: string folder }) return;
        e.Handled = true;
        FileLinks.Open(folder);
    }

    private void OpenProjectTerminalFolder(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (sender is not MenuItem { CommandParameter: string folder }) return;
        e.Handled = true;
        Runtime.Touch();
        ProjectTerminal.Open(folder);
    });

    private void NewOtherChat(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var folder = FolderPaths.DefaultChatFolder;
        Directory.CreateDirectory(folder);
        _model.NewFolderChat(folder);
    });

    private async void DeleteProject(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (sender is not FrameworkElement { DataContext: SessionTreeNode { CanDeleteProject: true } node }) return;
        var prompt = $"Permanently delete '{node.Name}' and all of its chats, including archived chats and their child conversations?\n\nProject folders and files stay on disk. This cannot be undone.";
        if (ShowMessage(prompt, "Delete project", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        var projectId = _model.ProjectForNode(node);
        await Runtime.DeleteProjectAsync(projectId, _windows.Sessions);
    });

    private async void ArchiveProject(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (sender is not FrameworkElement { DataContext: SessionTreeNode { CanCreateChat: true } node }) return;
        var projectId = _model.ProjectForNode(node);
        if (node.IsArchived)
        {
            _controller.Commit(next => AppController.Project(next, _model.ProfileId, projectId).IsArchived = false);
            if (_model.ShowArchived) _model.ToggleArchived();
            _model.SelectProject(projectId);
        }
        else await Runtime.ArchiveProjectAsync(projectId, _windows.Sessions);
    });

    private async void ArchiveOldProjectChats(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (sender is not FrameworkElement { DataContext: SessionTreeNode { CanCreateChat: true } node }) return;
        await Runtime.ArchiveOldChatsAsync(_model.ProjectForNode(node), _windows.Sessions);
    });

    private async void ArchivePreviousChats(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (sender is not FrameworkElement { DataContext: SessionListItem { CanArchivePrevious: true } row } || row.ProfileId != _model.ProfileId) return;
        var target = _controller.RegisterSession(row);
        await Runtime.ArchivePreviousChatsAsync(target.ProjectId, row, _windows.Sessions);
    });

    private async void DeleteProjectArchivedChats(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (sender is not FrameworkElement { DataContext: SessionTreeNode { CanCreateChat: true } node }) return;
        var prompt = $"Permanently delete all archived chats in '{node.Name}', their saved drafts, queued messages, Codex history, and any spawned child conversations?\n\nThe project, folders, and files stay on disk. This cannot be undone.";
        if (ShowMessage(prompt, "Delete archived project chats", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        await Runtime.DeleteArchivedChatsAsync(_model.ProjectForNode(node), _windows.Sessions);
    });

    private async void DeleteAllArchivedChats(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!_model.CanDeleteArchivedChats) return;
        var prompt = $"Permanently delete all archived chats across every project in '{_model.ProfileName}', their saved drafts, queued messages, Codex history, and any spawned child conversations?\n\nProjects, folders, and files stay on disk. This cannot be undone.";
        if (ShowMessage(prompt, "Delete all archived chats", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        await Runtime.DeleteArchivedChatsAsync(null, _windows.Sessions);
    });

    private void LoadMoreProjectSessions(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (sender is FrameworkElement { DataContext: SessionTreeNode node }) _model.LoadMoreProjectSessions(node);
    });

    private void OpenTreeOptions(object sender, MouseButtonEventArgs e)
    {
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != sender; current = GetTranscriptParent(current))
        {
            if (current is not TreeViewItem { DataContext: SessionTreeNode } item) continue;
            if (item.Template.FindName("PART_Header", item) is not DependencyObject header) return;
            var button = Descendants(header).OfType<Button>().FirstOrDefault(option => option.IsVisible && option.ContextMenu is not null);
            if (button is null) return;
            _projectDragHeader = null;
            ClearProjectDropMarker();
            e.Handled = true;
            OpenMenu(button, PlacementMode.MousePoint);
            return;
        }
    }

    private void ExpandProjectHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.ClickCount < 2) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != sender; current = GetTranscriptParent(current))
        {
            if (current is ButtonBase) return;
            if (current is not TreeViewItem item) continue;
            if (item.DataContext is SessionTreeNode { IsProject: true } node)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    e.Handled = true;
                    Guard(() => _model.ActivateTreeNode(node));
                }
                node.IsExpanded = true;
            }
            return;
        }
    }

    private void ProjectDragStart(object sender, MouseButtonEventArgs e)
    {
        _projectDragHeader = null;
        if (sender is not FrameworkElement { DataContext: SessionTreeNode { IsProject: true, IsFolderGroup: false } } header) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != header; current = GetTranscriptParent(current))
            if (current is ButtonBase) return;
        _projectDragStart = e.GetPosition(header); _projectDragHeader = header;
        e.Handled = true;
    }

    private void ProjectDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement header || header != _projectDragHeader || header.DataContext is not SessionTreeNode node) return;
        _projectDragHeader = null; e.Handled = true;
        Guard(() => _model.ActivateTreeNode(node));
    }

    private void ProjectDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _projectDragHeader = null; return; }
        if (sender is not FrameworkElement header || header != _projectDragHeader || header.DataContext is not SessionTreeNode node) return;
        var movement = e.GetPosition(header) - _projectDragStart;
        if (Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _projectDragHeader = null;
        try { DragDrop.DoDragDrop(header, new DataObject(ProjectDragFormat, node), DragDropEffects.Move); }
        finally { ClearProjectDropMarker(); }
        e.Handled = true;
    }

    private SessionTreeNode? DraggedProject(DragEventArgs e) => e.Data.GetDataPresent(ProjectDragFormat, false)
        && e.Data.GetData(ProjectDragFormat, false) is SessionTreeNode { IsProject: true, IsFolderGroup: false } node && node.ProfileId == _model.ProfileId ? node : null;

    private void ProjectDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (sender is not FrameworkElement { DataContext: SessionTreeNode { IsProject: true, IsFolderGroup: false } target } header
            || DraggedProject(e) is not { } source || source.Key == target.Key) { ClearProjectDropMarker(); return; }
        e.Effects = DragDropEffects.Move;
        var after = e.GetPosition(header).Y >= header.ActualHeight / 2;
        if (_projectDropAdorner is { } existing && existing.AdornedElement == header && existing.After == after) return;
        ClearProjectDropMarker();
        if (AdornerLayer.GetAdornerLayer(header) is { } layer)
        {
            _projectDropAdorner = new ProjectDropAdorner(header, after);
            layer.Add(_projectDropAdorner);
        }
    }

    private void ProjectDrop(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        ClearProjectDropMarker();
        if (sender is not FrameworkElement { DataContext: SessionTreeNode { IsProject: true, IsFolderGroup: false } target } header || DraggedProject(e) is not { } source) return;
        Guard(() => _model.MoveProject(source, target, e.GetPosition(header).Y >= header.ActualHeight / 2));
        e.Effects = DragDropEffects.Move;
    }

    private void ProjectDragLeave(object sender, DragEventArgs e) => ClearProjectDropMarker();
    private void ClearProjectDropMarker()
    {
        if (_projectDropAdorner is not { } marker) return;
        AdornerLayer.GetAdornerLayer(marker.AdornedElement)?.Remove(marker); _projectDropAdorner = null;
    }

    private sealed class ProjectDropAdorner : Adorner
    {
        public bool After { get; }
        public ProjectDropAdorner(UIElement element, bool after) : base(element) { After = after; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext context)
        {
            var y = After ? AdornedElement.RenderSize.Height : 0;
            context.DrawLine(new Pen((Brush)FindResource("AccentBrush"), 2), new Point(0, y), new Point(AdornedElement.RenderSize.Width, y));
        }
    }
}
