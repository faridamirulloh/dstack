using MiniCodex.Presentation;

namespace MiniCodex.Models;

public sealed record SkillSource(string Owner, string Repository, string Commit, string Folder)
{
    public string Url => $"https://github.com/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Repository)}/tree/{Commit}/" + string.Join('/', Folder.Split('/').Select(Uri.EscapeDataString));
    public string DirectoryName => Folder.Length == 0 ? Repository : Folder.Split('/').Last();
}

public sealed class ExtensionPackage : ObservableObject
{
    private bool _installed;
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required ReferenceKind Kind { get; init; }
    public string Description { get; init; } = "Description not provided by the catalog.";
    public string Source { get; init; } = "";
    public string Version { get; init; } = "";
    public string Compatibility { get; init; } = "";
    public string? PluginId { get; init; }
    public SkillSource? Skill { get; init; }
    public bool Available { get; init; } = true;
    public bool Enabled { get; init; }
    public bool Installed
    {
        get => _installed;
        set { if (_installed == value) return; _installed = value; Changed(nameof(Installed), nameof(CanInstall), nameof(InstallLabel), nameof(Detail)); }
    }
    public bool CanInstall => Available && !Installed;
    public string InstallLabel => Installed ? "Installed" : Available ? "Install" : "Unavailable";
    public string Detail => string.Join(" · ", new[] { Kind.ToString(), Source, Version, Installed ? "Installed" : "" }.Where(part => part.Length > 0));
    public string Tooltip => Description + "\n" + Compatibility + "\n" + (Skill?.Url ?? PluginId ?? Source);
}
