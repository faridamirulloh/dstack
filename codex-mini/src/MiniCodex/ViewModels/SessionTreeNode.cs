using System.Collections.ObjectModel;
using MiniCodex.Models;
using MiniCodex.Presentation;

namespace MiniCodex.ViewModels;

public sealed class SessionTreeNode(string key, bool isProject) : ObservableObject
{
    public const int SessionPageSize = 5;
    private bool _expanded;
    private bool _selected;
    public string Key { get; } = key;
    public bool IsProject { get; } = isProject;
    public bool IsSession => !IsProject && !IsLoadMore && !IsSection;
    public bool IsSection { get; private set; }
    public bool IsFolderGroup { get; private set; }
    public bool IsDeleting { get; private set; }
    public bool IsArchiving { get; private set; }
    public bool IsArchived { get; private set; }
    public string ArchiveProjectLabel => IsArchived ? "Restore project" : "Archive project";
    public string ArchiveProjectTooltip => IsArchived ? "Show this project in the active sidebar. Its chats stay archived."
        : "Archive all active chats and move this project to Archived chats.";
    public string? FolderPath { get; private set; }
    public bool CanCreateChat => IsProject && !IsDeleting && !IsArchiving && (ProjectId is not null || RemoteProjectId is not null || Path.IsPathFullyQualified(FolderPath ?? ""));
    public bool CanDeleteProject => IsProject && !IsDeleting && !IsArchiving;
    public string OpenLabel => IsFolderGroup ? "Open folder" : "Open project";
    public bool IsLoadMore => LoadMoreProject is not null;
    public SessionTreeNode? LoadMoreProject { get; private set; }
    public int VisibleSessionCount { get; private set; } = SessionPageSize;
    public Guid ProfileId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public string? RemoteProjectId { get; set; }
    public string Name { get; private set; } = "";
    private string _detail = "";
    public string Detail { get => _detail + (QueuedCount > 0 ? (_detail.Length > 0 ? " · " : "") + QueuedCount + " queued" : ""); private set => _detail = value; }
    public int QueuedCount { get; private set; }
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    public bool IsRunning { get; private set; }
    public string Activity { get; private set; } = "Ready";
    public bool IsPaused => Activity is "Waiting for approval" or "Waiting for your answer" or "Paused";
    public bool IsStopped => Activity is "Stopped" or "Failed";
    public string ActivityTooltip => Activity switch
    {
        "Waiting for approval" => "Waiting for approval. Open chat to review.",
        "Waiting for your answer" => "Waiting for your answer. Open chat to reply.",
        "Paused" => "Paused after a file conflict. Open chat to Continue or Cancel.",
        "Stopped" => "Task stopped. Open chat to view.",
        "Failed" => "Task failed. Open chat to view.",
        _ => Activity
    };
    public bool HasUnreadCompletion { get; private set; }
    public bool HasDraft { get; private set; }
    private string _tooltip = "";
    public string Tooltip { get => _tooltip + (IsPaused || IsStopped ? "\n" + ActivityTooltip : ""); private set => _tooltip = value; }
    public bool CanManageProject { get; private set; }
    public SessionListItem? Session { get; private set; }
    public ObservableCollection<SessionTreeNode> Children { get; } = [];
    public bool IsExpanded { get => _expanded; set { if (_expanded == value) return; _expanded = value; Changed(); } }
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(); } }

    public void ShowMoreSessions() => VisibleSessionCount += SessionPageSize;
    public void UpdateLoadMore(SessionTreeNode project, int remaining)
    {
        LoadMoreProject = project; ProfileId = project.ProfileId;
        Tooltip = $"Show the next {Math.Min(SessionPageSize, remaining)} sessions ({remaining} remaining)";
        Changed(nameof(IsLoadMore), nameof(IsSession), nameof(Tooltip));
    }
    public void UpdateSection(Guid profileId)
    {
        ProfileId = profileId; IsSection = true; Name = "OTHER CHATS";
        Tooltip = @"Chats from your Documents\Codex folder and its subfolders.";
        Changed(nameof(IsSection), nameof(IsSession), nameof(Name), nameof(Tooltip));
    }
    public void UpdateProject(Guid profileId, Guid? projectId, string name, string folders, bool canManage, int count,
        bool isFolderGroup = false, string? primaryFolder = null, bool isDeleting = false, bool isArchiving = false, bool isArchived = false)
    {
        ProfileId = profileId; ProjectId = projectId; Name = name; IsFolderGroup = isFolderGroup; FolderPath = primaryFolder; IsDeleting = isDeleting; IsArchiving = isArchiving;
        IsArchived = isArchived;
        CanManageProject = canManage && !isFolderGroup && !isDeleting && !isArchiving;
        Detail = count + (count == 1 ? " chat" : " chats"); Tooltip = folders;
        Changed(nameof(Name), nameof(Detail), nameof(HasDetail), nameof(Tooltip), nameof(CanManageProject), nameof(IsFolderGroup),
            nameof(IsDeleting), nameof(IsArchiving), nameof(IsArchived), nameof(ArchiveProjectLabel), nameof(ArchiveProjectTooltip), nameof(CanCreateChat), nameof(CanDeleteProject), nameof(OpenLabel));
    }
    public void UpdateSession(SessionListItem session)
    {
        Session = session; ProfileId = session.ProfileId; ProjectId = session.ProjectId; Name = session.Name;
        HasUnreadCompletion = session.HasUnreadCompletion;
        Detail = string.Join(" · ", new[] { session.IsPinned ? "Pinned" : "", session.ThreadId is null ? "Draft" : "", session.IsArchived ? "Archived" : "" }.Where(value => value.Length > 0));
        Tooltip = session.Name + "\n" + session.UpdatedAt.ToLocalTime().ToString("g");
        Changed(nameof(Session), nameof(Name), nameof(Detail), nameof(HasDetail), nameof(Tooltip), nameof(HasUnreadCompletion));
    }
    public void UpdateDraft(bool hasDraft)
    {
        if (HasDraft == hasDraft) return;
        HasDraft = hasDraft;
        Changed(nameof(HasDraft));
    }
    public void UpdateActivity(bool running, int queued, string activity, bool archiving = false, bool deleting = false)
    {
        running = running && activity is not ("Waiting for approval" or "Waiting for your answer" or "Paused" or "Stopped" or "Failed");
        if (IsRunning == running && QueuedCount == queued && Activity == activity && IsArchiving == archiving && IsDeleting == deleting) return;
        IsRunning = running; QueuedCount = queued; Activity = activity; IsArchiving = archiving; IsDeleting = deleting;
        Changed(nameof(IsRunning), nameof(IsPaused), nameof(IsStopped), nameof(IsArchiving), nameof(IsDeleting), nameof(Activity), nameof(ActivityTooltip), nameof(QueuedCount), nameof(Detail), nameof(HasDetail), nameof(Tooltip));
    }
}
