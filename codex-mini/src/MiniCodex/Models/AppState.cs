using System.Text.Json.Serialization;
using MiniCodex.Services;

namespace MiniCodex.Models;

public sealed class AppState
{
    public int Version { get; set; } = 1;
    public List<ProfileState> Profiles { get; set; } = [];
    public string? CodexExecutable { get; set; }
    public double UiZoom { get; set; } = 1;
    public List<List<Guid>> AccountWindowLayouts { get; set; } = [];
}

public sealed class ProfileState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Account 1";
    public string Effort { get; set; } = "Medium";
    public bool FastMode { get; set; }
    public string BusySendMode { get; set; } = "Ask";
    public string ApprovalMode { get; set; } = "Ask me";
    public string? SelectedModel { get; set; }
    public string? ExistingCodexHome { get; set; }
    public List<ProjectState> Projects { get; set; } = [];
    public bool OrderProjectsByLastActive { get; set; } = true;
    public List<string> CustomProjectOrder { get; set; } = [];
    [JsonIgnore] public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0])).ToUpperInvariant();
}

public sealed class ProjectState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New project";
    public string? CodexProjectId { get; set; }
    public bool IsArchived { get; set; }
    public bool? IsUnassigned { get; set; }
    public bool? IsImportedFolder { get; set; }
    public List<string> Folders { get; set; } = [];
    public List<string> ExcludedCodexFolders { get; set; } = [];
    public string PrimaryFolder { get; set; } = "";
    public List<ChatState> Chats { get; set; } = [];
    // Persisted task-start time; opening a project or chat does not change it.
    public DateTimeOffset LastActiveAt { get; set; }
    [JsonIgnore] public string FolderSummary => $"{Folders.Count} folder{(Folders.Count == 1 ? "" : "s")} · shared by every chat";
    [JsonIgnore] public string FolderTooltip => "Primary: " + PrimaryFolder + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, Folders);
    // Folder placement follows the current location policy, including records saved by older versions.
    [JsonIgnore] public bool IsFolderGroup => Folders.Count > 0 && Folders.All(FolderPaths.IsDefaultChatFolder);
}

public sealed class ChatState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New chat";
    public bool HasCustomName { get; set; }
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public bool HasUnreadCompletion { get; set; }
    public FileConflictPause? ConflictPause { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CodexThreadId { get; set; }
    public string? NativeProjectId { get; set; }
    public string? WorkingDirectory { get; set; }
    [JsonIgnore] public string Subtitle => (IsPinned ? "Pinned · " : "") + (IsArchived ? "Archived" : CodexThreadId is null ? "Draft" : "Codex chat");
}

public sealed record FileConflictBlocker(Guid ProfileId, Guid ChatId, Guid TaskRunId, string Name);
public sealed record FileConflictPause(IReadOnlyList<string> Files, IReadOnlyList<FileConflictBlocker> Blockers);

public sealed record FolderPlan(string Name, IReadOnlyList<string> Folders, string PrimaryFolder);
public sealed record ProjectWorkspace(string PrimaryDirectory, IReadOnlyList<string> Folders);

public sealed record FolderOption(string Path)
{
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path)) is { Length: > 0 } name ? name : Path;
    public string Availability => Directory.Exists(Path) ? "Available" : "Unavailable";
    public string Display => $"{Name}  ·  {Path}";
}
