using System.Collections.ObjectModel;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.ViewModels;

public sealed class ExtensionBrowserViewModel : ObservableObject, IDisposable
{
    private readonly AccountViewModel _account;
    private CancellationTokenSource _lifetime = new();
    private IReadOnlyList<ExtensionPackage> _plugins = [], _skills = [];
    private readonly Dictionary<string, ExtensionPackage> _previews = new(StringComparer.OrdinalIgnoreCase);
    private Task? _load;
    private string _context = "", _cwd = "", _home = "", _search = "", _filter = "All", _url = "";
    private int _version;
    private bool _disposed, _pluginsLoaded, _skillsLoaded, _installing, _needsRefresh;
    public ObservableCollection<ExtensionPackage> Packages { get; } = [];
    public IReadOnlyList<ExtensionPackage> Plugins => _plugins;
    public string AccountName => _account.ProfileName;
    public string Destination => _home;
    public string DestinationTooltip => "Plugins use Codex's account configuration. Standalone skills install in " + Path.Combine(_home, "skills") + ".";
    public string Notice { get; private set; } = "Browse Codex marketplaces and OpenAI's curated skills, or preview a public GitHub skill.";
    public bool Loading { get; private set; }
    public bool Working => Loading || _installing || _account.Runtime.InstallingExtensions;
    public bool CanBrowse => !_disposed && !Loading && !_installing && !_account.Runtime.InstallingExtensions && _cwd.Length > 0;
    public bool CanInstall => CanBrowse && !_account.Runtime.HasWork && !_account.Runtime.LoginPending && !_needsRefresh;
    public string Activity => _installing || _account.Runtime.InstallingExtensions ? "Installing extension…"
        : Loading ? "Loading catalog…" : _account.Runtime.HasWork ? "Installation is available when this account's tasks finish."
        : _needsRefresh ? "Refresh the plugin catalog before another installation." : Packages.Count + " entries";
    public string Search { get => _search; set { if (_search == value) return; _search = value; Rebuild(); } }
    public string Filter { get => _filter; set { if (_filter == value) return; _filter = value; Changed(); Rebuild(); } }
    public string GitHubUrl { get => _url; set { _url = value; Changed(); } }

    public ExtensionBrowserViewModel(AccountViewModel account) => _account = account;
    public void SyncContext()
    {
        if (_disposed) return;
        var home = _account.Runtime.HomePath;
        var cwd = _account.SelectedProject?.PrimaryFolder ?? "";
        var context = home + "|" + cwd;
        if (context != _context)
        {
            _context = context; _home = home; _cwd = cwd; _version++;
            _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
            _load = null; _pluginsLoaded = _skillsLoaded = _needsRefresh = Loading = false;
            _plugins = []; _skills = []; _previews.Clear(); Rebuild();
            Notice = cwd.Length == 0 ? "Select a project to browse extensions." : "Browse Codex marketplaces and OpenAI's curated skills, or preview a public GitHub skill.";
            Changed(nameof(Plugins), nameof(Destination), nameof(DestinationTooltip), nameof(Notice));
        }
        Changed(nameof(AccountName)); NotifyState();
    }
    public Task EnsureLoadedAsync(bool includeSkills, bool refresh = false)
    {
        if (_disposed || _cwd.Length == 0 || _account.Runtime.InstallingExtensions) return Task.CompletedTask;
        if (_load is { IsCompleted: false }) return ContinueLoadAsync(_load, includeSkills, refresh);
        if (!refresh && _pluginsLoaded && (!includeSkills || _skillsLoaded)) return Task.CompletedTask;
        return _load = LoadAsync(includeSkills, refresh, _version, _lifetime.Token);
    }
    private async Task ContinueLoadAsync(Task current, bool includeSkills, bool refresh)
    {
        await current;
        if (!_disposed && (refresh || includeSkills && !_skillsLoaded)) await EnsureLoadedAsync(includeSkills, refresh);
    }
    private static async Task<(IReadOnlyList<ExtensionPackage> Packages, string Error)> ReadAsync(Func<Task<IReadOnlyList<ExtensionPackage>>> read)
    {
        try { return (await read(), ""); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return (Array.Empty<ExtensionPackage>(), error.Message); }
    }
    private async Task LoadAsync(bool includeSkills, bool refresh, int version, CancellationToken cancellation)
    {
        Loading = true; Notice = "Loading the selected account's plugin catalog" + (includeSkills ? " and curated skills…" : "…");
        Changed(nameof(Notice)); NotifyState();
        try
        {
            var plugins = ReadAsync(() => _account.Runtime.ListExtensionPluginsAsync(_cwd, cancellation));
            Task<(IReadOnlyList<ExtensionPackage> Packages, string Error)> skills = includeSkills && (refresh || !_skillsLoaded)
                ? ReadAsync(() => GitHubSkills.ListCuratedAsync(_home, cancellation))
                : Task.FromResult((Packages: _skills, Error: ""));
            await Task.WhenAll(plugins, skills);
            if (_disposed || version != _version) return;
            var pluginResult = await plugins; var skillResult = await skills;
            if (pluginResult.Error.Length == 0) { _plugins = pluginResult.Packages; _pluginsLoaded = true; _needsRefresh = false; }
            else _pluginsLoaded = false;
            if (includeSkills && skillResult.Error.Length == 0) { _skills = skillResult.Packages; _skillsLoaded = true; }
            var errors = new[] { pluginResult.Error.Length > 0 ? "Plugins: " + pluginResult.Error : "", skillResult.Error.Length > 0 ? "Skills: " + skillResult.Error : "" }.Where(value => value.Length > 0);
            Notice = string.Join("\n", errors);
            if (Notice.Length == 0) Notice = "Install only extensions you trust. Plugins use Codex's marketplace policy. Skills are downloaded from a pinned public GitHub commit; existing folders are never overwritten.";
            Rebuild(); Changed(nameof(Plugins), nameof(Notice));
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && version == _version) { Notice = "Catalog loading timed out or was canceled. Choose Refresh to retry."; Changed(nameof(Notice)); }
        }
        finally { if (!_disposed && version == _version) { Loading = false; NotifyState(); } }
    }
    private void Rebuild()
    {
        Packages.Clear();
        foreach (var package in _plugins.Concat(_skills).Concat(_previews.Values).GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.Last())
            .Where(item => (_filter == "All" || item.Kind.ToString() + "s" == _filter)
                && (_search.Length == 0 || (item.Name + " " + item.Source + " " + item.Description).Contains(_search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Kind).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)) Packages.Add(package);
        NotifyState();
    }
    public async Task<ExtensionPackage?> PreviewAsync()
    {
        if (!CanBrowse) return null;
        var version = _version;
        Loading = true; Notice = "Reading public GitHub skill metadata…"; Changed(nameof(Notice)); NotifyState();
        try
        {
            var package = await GitHubSkills.PreviewAsync(GitHubUrl, _home, _lifetime.Token);
            if (_disposed || version != _version) return null;
            _previews[package.Key] = package; Filter = "Skills"; Search = "";
            Notice = "Previewed " + package.Name + " at commit " + package.Version + ". Choose Install to add it to this account.";
            return package;
        }
        catch (Exception error) { if (!_disposed && version == _version) Notice = "Could not preview skill. " + error.Message; return null; }
        finally { if (!_disposed && version == _version) { Loading = false; Changed(nameof(Notice)); NotifyState(); } }
    }
    public async Task InstallAsync(ExtensionPackage package)
    {
        if (!CanInstall || !package.CanInstall) return;
        var version = _version; var cwd = _cwd;
        _installing = true; Notice = "Installing " + package.Name + " for " + AccountName + "…"; Changed(nameof(Notice)); NotifyState();
        string result;
        try { result = await _account.Runtime.InstallExtensionAsync(package, cwd); }
        catch (Exception error)
        {
            result = "Could not install " + package.Name + ". " + error.Message;
            if (package.Kind == ReferenceKind.Plugin) _needsRefresh = true;
        }
        finally { _installing = false; if (!_disposed) NotifyState(); }
        if (_disposed || version != _version) return;
        // Recheck CLI state after an install, including a command with an unknown outcome.
        await EnsureLoadedAsync(false, true);
        foreach (var skill in _skills.Concat(_previews.Values).Where(item => item.Skill is not null))
            skill.Installed = File.Exists(Path.Combine(_home, "skills", skill.Skill!.DirectoryName, "SKILL.md"));
        if (_disposed || version != _version) return;
        Notice = result + (_pluginsLoaded ? "" : "\nPlugin status could not be refreshed. Use Refresh before retrying.");
        Changed(nameof(Notice)); NotifyState();
    }
    private void NotifyState() => Changed(nameof(Loading), nameof(Working), nameof(CanBrowse), nameof(CanInstall), nameof(Activity));
    public void Dispose()
    {
        _disposed = true; _version++;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}
