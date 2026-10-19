namespace MiniCodex.Models;

public sealed record SessionListItem(Guid ProfileId, string ProfileName, Guid? ProjectId, Guid? ChatId,
    string? ThreadId, string Name, string ProjectName, string PrimaryFolder, IReadOnlyList<string> Folders,
    bool IsPinned, bool IsArchived, DateTimeOffset UpdatedAt, bool IsCurrentAccount, string? CodexProjectId = null, string? WorkingDirectory = null, bool IsUnassigned = false, bool IsActive = false, string? NativeProjectId = null)
{
    public bool HasUnreadCompletion { get; init; }
    public string Key => ProfileId.ToString("N") + "/" + (ThreadId ?? ChatId?.ToString("N"));
    public string Subtitle => ProjectName + (IsPinned ? " · Pinned" : "") + (ThreadId is null ? " · Draft" : "");
    public string OpenLabel => "Open chat";
    public bool CanManage => IsCurrentAccount && ProjectId is not null && ChatId is not null;
    public bool CanArchivePrevious => IsCurrentAccount && !IsArchived;
}

public sealed record CodexProject(string Id, string Name, IReadOnlyList<string> Folders, long Position);
