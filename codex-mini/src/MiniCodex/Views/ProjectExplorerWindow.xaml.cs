using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.ViewModels;

namespace MiniCodex.Views;

public partial class ProjectExplorerWindow : Window
{
    private readonly ProjectExplorerViewModel _model;
    private readonly Action<ComposerReference> _insert;
    private bool _files = true;
    private bool _browse;
    private ComposerReference? _selected;
    public ProjectExplorerWindow(ProjectExplorerViewModel model, Action<ComposerReference> insert, bool files)
    {
        _model = model; _insert = insert;
        InitializeComponent(); DataContext = model;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        model.PropertyChanged += ModelChanged;
        model.Catalog.PropertyChanged += CatalogChanged;
        Closed += (_, _) => { model.PropertyChanged -= ModelChanged; model.Catalog.PropertyChanged -= CatalogChanged; };
        Loaded += (_, _) => SetTab(files);
    }
    public void SetTab(bool files)
    {
        _files = files; _selected = null;
        FilesPanel.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        CapabilitiesPanel.Visibility = files ? Visibility.Collapsed : Visibility.Visible;
        FilesTab.Foreground = (System.Windows.Media.Brush)FindResource(files ? "AccentBrush" : "MutedBrush");
        CapabilitiesTab.Foreground = (System.Windows.Media.Brush)FindResource(files ? "MutedBrush" : "AccentBrush");
        UpdateState();
        if (!files)
        {
            _ = _model.EnsureCapabilitiesAsync();
            SetCatalogTab(_browse);
        }
    }
    private void CatalogChanged(object? sender, PropertyChangedEventArgs e) => UpdateState();
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectExplorerViewModel.Entries))
        {
            _selected = null;
            if (!_files && IsVisible)
            {
                _ = _model.EnsureCapabilitiesAsync();
                _ = _model.Catalog.EnsureLoadedAsync(_browse);
            }
        }
        UpdateState();
    }
    private void UpdateState()
    {
        ExplorerNotice.Text = _files ? _model.IsFileSearching ? _model.FileNotice : "All attached project folders. Expand a folder to load its contents. Links and junctions are not followed. Right-click for Open or Show in Explorer."
            : _browse ? _model.Catalog.Notice : _model.Notice;
        ExplorerNotice.ToolTip = ExplorerNotice.Text;
        MentionSelected.IsEnabled = _model.CanInsert && _selected?.Enabled == true;
        MentionSelected.Visibility = !_files && _browse ? Visibility.Collapsed : Visibility.Visible;
        FooterText.Text = !_files && _browse ? "Install adds the selected extension to this account. Service connections require their own sign-in." : "Mentions are inserted into the current chat's draft.";
    }
    private void SetCatalogTab(bool browse)
    {
        _browse = browse; _selected = null;
        BrowsePanel.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
        CapabilityTree.Visibility = browse ? Visibility.Collapsed : Visibility.Visible;
        CatalogFilter.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
        InstalledTab.Foreground = (System.Windows.Media.Brush)FindResource(browse ? "MutedBrush" : "AccentBrush");
        BrowseTab.Foreground = (System.Windows.Media.Brush)FindResource(browse ? "AccentBrush" : "MutedBrush");
        UpdateState();
        _ = _model.Catalog.EnsureLoadedAsync(browse);
    }
    private void ShowInstalled(object sender, RoutedEventArgs e) => SetCatalogTab(false);
    private void ShowBrowse(object sender, RoutedEventArgs e) => SetCatalogTab(true);
    private async void PreviewSkill(object sender, RoutedEventArgs e)
    {
        if (!_model.Catalog.CanBrowse) return;
        _model.Search = "";
        if (await _model.Catalog.PreviewAsync() is { } package)
        {
            CatalogList.SelectedItem = package;
            CatalogList.ScrollIntoView(package);
        }
    }
    private async void InstallExtension(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ExtensionPackage package) return;
        await _model.Catalog.InstallAsync(package);
        await _model.EnsureCapabilitiesAsync(true);
    }
    private void ShowFiles(object sender, RoutedEventArgs e) => SetTab(true);
    private void ShowCapabilities(object sender, RoutedEventArgs e) => SetTab(false);
    private async void Refresh(object sender, RoutedEventArgs e)
    {
        _selected = null; UpdateState();
        if (_files) _model.RefreshFiles();
        else
        {
            await Task.WhenAll(_model.EnsureCapabilitiesAsync(true), _model.Catalog.EnsureLoadedAsync(_browse, true));
        }
    }
    private void FileSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { _selected = (e.NewValue as ProjectFileNode)?.Reference; UpdateState(); }
    private void SearchFileSelected(object sender, SelectionChangedEventArgs e) { _selected = (sender as ListBox)?.SelectedItem as ComposerReference; UpdateState(); }
    private void SearchFileDoubleClick(object sender, MouseButtonEventArgs e) { if ((sender as ListBox)?.SelectedItem is ComposerReference reference) { Insert(reference); e.Handled = true; } }
    private void CapabilitySelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { _selected = (e.NewValue as CapabilityNode)?.Reference; UpdateState(); }
    private void Insert(ComposerReference? reference)
    {
        if (!_model.CanInsert || reference?.Enabled != true) return;
        try
        {
            if (reference.Kind is ReferenceKind.File or ReferenceKind.Folder) ProjectFiles.Validate(reference.Path, _model.Folders);
            _insert(reference);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { AppDialog.Show(this, error.Message, "Could not mention item", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void InsertSelected(object sender, RoutedEventArgs e) => Insert(_selected);
    private static ComposerReference? FileReference(object sender) => (sender as FrameworkElement)?.DataContext switch { ProjectFileNode node => node.Reference, ComposerReference reference => reference, _ => null };
    private void MentionFile(object sender, RoutedEventArgs e) => Insert(FileReference(sender));
    private void MentionCapability(object sender, RoutedEventArgs e) => Insert(((sender as FrameworkElement)?.DataContext as CapabilityNode)?.Reference);
    private async void LoadMore(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is ProjectFileNode node) await node.LoadMoreAsync(); }
    private void FileDoubleClick(object sender, MouseButtonEventArgs e)
    { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(null, source) is TreeViewItem { DataContext: ProjectFileNode { Reference.Kind: ReferenceKind.File } node }) { Insert(node.Reference); e.Handled = true; } }
    private void CapabilityDoubleClick(object sender, MouseButtonEventArgs e)
    { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(null, source) is TreeViewItem { DataContext: CapabilityNode { Reference.Kind: ReferenceKind.Skill or ReferenceKind.Connector } node }) { Insert(node.Reference); e.Handled = true; } }
    private void FileAction(object sender, bool reveal)
    {
        if (FileReference(sender) is not { } reference) return;
        try
        {
            ProjectFiles.Validate(reference.Path, _model.Folders);
            if (reveal) FileLinks.ShowInExplorer(reference.Path); else FileLinks.Open(reference.Path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { AppDialog.Show(this, error.Message, "Could not open item", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void OpenFile(object sender, RoutedEventArgs e) => FileAction(sender, false);
    private void RevealFile(object sender, RoutedEventArgs e) => FileAction(sender, true);
}
