using System.Collections.ObjectModel;
using System.ComponentModel;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.ViewModels;

public sealed class CapabilityNode(string name, ComposerReference? reference = null) : ObservableObject
{
    private bool _expanded = true;
    private bool _selected;
    public string Name { get; } = name;
    public ComposerReference? Reference { get; } = reference;
    public string Detail => Reference?.Detail ?? (Children.Count + " entries");
    public string Tooltip => Reference?.Tooltip ?? Name;
    public bool CanMention => Reference?.Enabled == true;
    public ObservableCollection<CapabilityNode> Children { get; } = [];
    public bool IsExpanded { get => _expanded; set { _expanded = value; Changed(); } }
    public bool IsSelected { get => _selected; set { _selected = value; Changed(); } }
}

public sealed class ProjectFileNode : ObservableObject
{
    private readonly IReadOnlyList<string> _roots;
    private readonly CancellationToken _cancellation;
    private readonly ProjectFileNode? _parent;
    private bool _expanded;
    private bool _selected;
    private bool _loaded;
    private bool _loading;
    private int _offset;
    public ComposerReference? Reference { get; }
    public string Name { get; }
    public string Detail { get; private set; } = "";
    public string Tooltip => Reference?.Path ?? Detail;
    public bool CanMention => Reference is not null;
    public bool CanLoad => _parent is not null;
    public ObservableCollection<ProjectFileNode> Children { get; } = [];
    public bool IsExpanded
    {
        get => _expanded;
        set { _expanded = value; Changed(); if (value && !_loaded) _ = LoadAsync(); }
    }
    public bool IsSelected { get => _selected; set { _selected = value; Changed(); } }

    public ProjectFileNode(ComposerReference reference, IReadOnlyList<string> roots, CancellationToken cancellation)
    {
        Reference = reference; Name = reference.Name; Detail = reference.KindLabel; _roots = roots; _cancellation = cancellation;
        if (reference.Kind == ReferenceKind.Folder) Children.Add(new ProjectFileNode("Expand to load", this, false));
    }
    private ProjectFileNode(string name, ProjectFileNode parent, bool loadable)
    {
        Name = name; _roots = parent._roots; _cancellation = parent._cancellation;
        _parent = loadable ? parent : null; _loaded = true;
    }
    public Task LoadMoreAsync() => _parent?.LoadAsync() ?? Task.CompletedTask;
    private async Task LoadAsync()
    {
        if (_loading || _cancellation.IsCancellationRequested || Reference?.Kind != ReferenceKind.Folder) return;
        _loading = true; Detail = "Loading…"; Changed(nameof(Detail));
        try
        {
            var page = await Task.Run(() => ProjectFiles.List(Reference.Path, _roots, _offset, _cancellation), _cancellation);
            if (_cancellation.IsCancellationRequested) return;
            foreach (var placeholder in Children.Where(child => child.Reference is null).ToArray()) Children.Remove(placeholder);
            foreach (var entry in page.Entries) Children.Add(new ProjectFileNode(entry, _roots, _cancellation));
            _offset += page.Entries.Count; _loaded = true;
            if (page.HasMore) Children.Add(new ProjectFileNode("Load next 300…", this, true));
            if (page.Notice.Length > 0) Children.Add(new ProjectFileNode("Retry: " + page.Notice, this, true));
            Detail = page.Notice.Length > 0 ? "Unavailable" : _offset + (page.HasMore ? "+ items" : " items");
        }
        catch (OperationCanceledException) { }
        finally { _loading = false; Changed(nameof(Detail)); }
    }
}

public sealed class ProjectExplorerViewModel : ObservableObject, IDisposable
{
    private readonly AccountViewModel _account;
    private CancellationTokenSource _filesLifetime = new();
    private CancellationTokenSource _capabilityLifetime = new();
    private CancellationTokenSource? _fileSearchLifetime;
    private string _fileSearch = "";
    private string _folderKey = "";
    private string _capabilityKey = "";
    private int _version;
    private bool _disposed;
    private Task? _load;
    private bool _loaded;
    private string _search = "";
    private IReadOnlyList<ComposerReference> _entries = [];
    public ObservableCollection<ProjectFileNode> Roots { get; } = [];
    public ObservableCollection<ComposerReference> FileResults { get; } = [];
    public string FileSearch
    {
        get => _fileSearch;
        set { if (_fileSearch == value) return; _fileSearch = value; Changed(nameof(FileSearch), nameof(IsFileSearching)); StartFileSearch(); }
    }
    public bool IsFileSearching => !string.IsNullOrWhiteSpace(FileSearch);
    public string FileNotice { get; private set; } = "";
    public ObservableCollection<CapabilityNode> CapabilityGroups { get; } = [];
    public ExtensionBrowserViewModel Catalog { get; }
    public string ProjectName => _account.ProjectName;
    public bool CanInsert => _account.CanCompose;
    public IReadOnlyList<string> Folders => (_account.SelectedProject?.Folders ?? []).Concat(
        Path.IsPathFullyQualified(_account.PrimaryFolder) ? new[] { _account.PrimaryFolder } : Array.Empty<string>()).Distinct(FolderPaths.Comparer).ToArray();
    public string Notice { get; private set; } = "Open Plugins & skills to discover this account's capabilities.";
    public bool Loading { get; private set; }
    public string Search { get => _search; set { _search = value; Catalog.Search = value; Changed(); RebuildGroups(); } }
    public IReadOnlyList<ComposerReference> Entries => _entries;

    public ProjectExplorerViewModel(AccountViewModel account)
    {
        _account = account;
        Catalog = new(account);
        Catalog.PropertyChanged += CatalogChanged;
        account.PropertyChanged += AccountChanged;
        account.Runtime.CapabilitiesChanged += CapabilitiesChanged;
        account.Runtime.Updated += RuntimeUpdated;
        SyncContext();
    }
    private void AccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountViewModel.SelectedProject) or nameof(AccountViewModel.SelectedChat) or nameof(AccountViewModel.PrimaryFolder)) SyncContext();
    }
    private void RuntimeUpdated(object? sender, EventArgs e) => SyncContext();
    private void CatalogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExtensionBrowserViewModel.Plugins)) RebuildGroups();
    }
    private void CapabilitiesChanged(object? sender, EventArgs e)
    {
        if (Loading) return;
        Invalidate();
        _ = EnsureCapabilitiesAsync(true);
    }
    private void SyncContext()
    {
        if (_disposed) return;
        Catalog.SyncContext();
        var project = _account.SelectedProject;
        var folders = project is null ? "" : project.Id + "|" + _account.PrimaryFolder + "|" + string.Join("|", project.Folders);
        if (folders != _folderKey)
        {
            _folderKey = folders;
            RefreshFiles();
        }
        var capabilities = folders + "|" + _account.SelectedChat?.CodexThreadId + "|" + _account.Runtime.Connected + "|" + _account.Runtime.SignedIn + "|" + _account.Runtime.Identity;
        if (_capabilityKey != capabilities)
        {
            _capabilityKey = capabilities; Invalidate();
            if (_account.Runtime.Connected && _account.Runtime.SignedIn) _ = EnsureCapabilitiesAsync();
        }
        Changed(nameof(ProjectName), nameof(CanInsert));
    }
    public void RefreshFiles()
    {
        _filesLifetime.Cancel(); _filesLifetime.Dispose(); _filesLifetime = new(); Roots.Clear();
        foreach (var folder in Folders)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            Roots.Add(new ProjectFileNode(new(ReferenceKind.Folder, name.Length > 0 ? name : folder, folder, "Project root"), Folders, _filesLifetime.Token));
        }
        StartFileSearch();
    }
    private void StartFileSearch()
    {
        _fileSearchLifetime?.Cancel(); _fileSearchLifetime?.Dispose(); _fileSearchLifetime = new();
        FileResults.Clear(); FileNotice = ""; Changed(nameof(FileNotice));
        if (IsFileSearching) _ = SearchFilesAsync(FileSearch.Trim(), Folders, _fileSearchLifetime.Token);
    }
    private async Task SearchFilesAsync(string query, IReadOnlyList<string> folders, CancellationToken cancellation)
    {
        try
        {
            FileNotice = "Searching all project folders…"; Changed(nameof(FileNotice));
            await Task.Delay(220, cancellation);
            var result = await Task.Run(() => ProjectFiles.Find(folders, query, cancellation, explorerSearch: true), cancellation);
            if (cancellation.IsCancellationRequested || _disposed) return;
            foreach (var entry in result.Entries) FileResults.Add(entry);
            FileNotice = result.Entries.Count + " matches" + (result.Notice.Length > 0 ? " · " + result.Notice : "");
            Changed(nameof(FileNotice));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && !_disposed) { FileNotice = error.Message; Changed(nameof(FileNotice)); }
        }
    }
    private void Invalidate()
    {
        _capabilityLifetime.Cancel(); _capabilityLifetime.Dispose(); _capabilityLifetime = new();
        _version++; _loaded = false; _load = null; Loading = false; _entries = []; CapabilityGroups.Clear();
        Notice = !_account.Runtime.SignedIn ? "Connect and sign in to discover this account's skills and plugins." : "Refresh to discover capabilities for this project and account.";
        Changed(nameof(Entries), nameof(Loading), nameof(Notice));
    }
    public Task EnsureCapabilitiesAsync(bool forceReload = false)
    {
        if (_disposed || _account.SelectedProject is null) return Task.CompletedTask;
        if (_load is { IsCompleted: false }) return forceReload ? ReloadAfterAsync(_load, _version) : _load;
        if (_loaded && !forceReload) return Task.CompletedTask;
        return _load = LoadCapabilitiesAsync(_version, forceReload, _capabilityLifetime.Token);
    }
    private async Task ReloadAfterAsync(Task pending, int version)
    {
        await pending;
        if (!_disposed && version == _version) await EnsureCapabilitiesAsync(true);
    }
    private async Task LoadCapabilitiesAsync(int version, bool forceReload, CancellationToken cancellation)
    {
        if (_account.SelectedProject is not { } project) return;
        Loading = true; Notice = "Discovering skills, plugins, and connectors…"; Changed(nameof(Loading), nameof(Notice));
        try
        {
            var snapshot = await _account.Runtime.DiscoverCapabilitiesAsync(project, _account.SelectedChat?.CodexThreadId, forceReload, cancellation);
            if (_disposed || version != _version) return;
            _entries = snapshot.Entries; _loaded = true;
            Notice = snapshot.Notice.Length > 0 ? snapshot.Notice : _entries.Count == 0 ? "No skills, configured plugins, or connectors were reported for this account and project."
                : "Uses this account's Codex home and project configuration. Mention an entry to add it to the draft.";
            RebuildGroups(); Changed(nameof(Entries));
        }
        catch (Exception error)
        {
            if (!_disposed && version == _version) Notice = error.Message;
        }
        finally
        {
            if (!_disposed && version == _version) { Loading = false; Changed(nameof(Loading), nameof(Notice)); }
        }
    }
    private void RebuildGroups()
    {
        CapabilityGroups.Clear();
        bool Matches(ComposerReference reference) => _search.Length == 0 || (reference.Name + " " + reference.Source + " " + reference.Description).Contains(_search, StringComparison.OrdinalIgnoreCase);
        var plugins = _entries.Where(reference => reference.Kind == ReferenceKind.Plugin).ToList();
        foreach (var installed in Catalog.Plugins.Where(item => item.Installed && !plugins.Any(entry => entry.PluginId == item.PluginId)))
            plugins.Add(new(ReferenceKind.Plugin, installed.Name, "plugin://" + installed.PluginId, installed.Description
                + (installed.Enabled ? " Installed; reconnect if Codex has not exposed its capabilities yet." : " Installed but disabled in Codex configuration."), false, installed.Source, installed.PluginId));
        foreach (var plugin in plugins)
        {
            var children = _entries.Where(reference => reference.Kind == ReferenceKind.Skill && reference.PluginId == plugin.PluginId).ToArray();
            if (!Matches(plugin) && !children.Any(Matches)) continue;
            var node = new CapabilityNode(plugin.Name, plugin);
            foreach (var skill in children.Where(reference => Matches(plugin) || Matches(reference))) node.Children.Add(new(skill.Name, skill));
            CapabilityGroups.Add(node);
        }
        foreach (var kind in new[] { ReferenceKind.Skill, ReferenceKind.Connector })
        {
            var group = new CapabilityNode(kind == ReferenceKind.Skill ? "Standalone skills" : "Connectors");
            foreach (var entry in _entries.Where(reference => reference.Kind == kind && (kind != ReferenceKind.Skill || reference.PluginId is null) && Matches(reference))) group.Children.Add(new(entry.Name, entry));
            if (group.Children.Count > 0) CapabilityGroups.Add(group);
        }
    }
    public void Dispose()
    {
        _disposed = true; _version++; _filesLifetime.Cancel(); _filesLifetime.Dispose();
        _capabilityLifetime.Cancel(); _capabilityLifetime.Dispose();
        _fileSearchLifetime?.Cancel(); _fileSearchLifetime?.Dispose();
        Catalog.PropertyChanged -= CatalogChanged; Catalog.Dispose();
        _account.PropertyChanged -= AccountChanged; _account.Runtime.CapabilitiesChanged -= CapabilitiesChanged; _account.Runtime.Updated -= RuntimeUpdated;
    }
}
