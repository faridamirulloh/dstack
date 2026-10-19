using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.Codex;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MiniCodex.ViewModels;

public sealed class AccountViewModel : ObservableObject, IDisposable
{
    private readonly AppController _controller;
    private readonly SessionCatalog _sessions;
    private readonly DraftStore _draftStore;
    private IReadOnlyList<SessionListItem> _sessionRows = [];
    private readonly Dictionary<string, SessionTreeNode> _treeNodes = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SessionTreeNode> _orderedProjects = [];
    private readonly Dictionary<Guid, string> _drafts;
    private readonly Dictionary<Guid, List<ComposerReference>> _draftReferences;
    private readonly Dictionary<Guid, ObservableCollection<ChatAttachment>> _attachments;
    private readonly Dictionary<Guid, ObservableCollection<ChatAnnotation>> _annotations;
    private readonly Dictionary<Guid, long> _draftVersions;
    private readonly ObservableCollection<ChatAttachment> _emptyAttachments = [];
    private readonly ObservableCollection<ChatAnnotation> _emptyAnnotations = [];
    private readonly ObservableCollection<TranscriptEntry> _emptyTranscript = [];
    private Guid? _pinnedChat;
    private int _mentionVersion = -1;
    private Guid? _mentionDraftChat;
    private int _mentionDraftReferenceCount = -1;
    private IReadOnlyList<ComposerReference>? _explorerMentions;
    private IReadOnlyList<ComposerReference> _mentionCache = [];
    private Guid? _projectId;
    private Guid? _chatId;
    private readonly List<Guid> _chatHistory = [];
    private int _chatHistoryIndex = -1;
    private string _search = "";
    private string _findQuery = "";
    public string FindQuery { get => _findQuery; set { if (_findQuery == value) return; _findQuery = value; Changed(); } }
    private bool _searchExpanded;
    private bool _showArchived;
    private bool _wasSignedIn;
    private bool _disposed;
    private bool _creatingChat;
    private readonly ObservableCollection<ChatMessage> _emptyMessages = [];
    public AccountRuntime Runtime { get; }
    public ProjectExplorerViewModel Explorer { get; }
    public event Action<SessionListItem>? SessionRequested;
    public ObservableCollection<SessionTreeNode> SessionProjects { get; } = [];
    public bool IsUpdatingTree { get; private set; }
    public string SaveNotice { get; private set; } = "";
    public Guid ProfileId { get; }
    private ProfileState Profile => _controller.Profile(ProfileId);
    public string ProfileName => Profile.Name;
    public string Initials => Profile.Initials;
    public bool OrderProjectsByLastActive => Profile.OrderProjectsByLastActive;
    public void UseLastActiveProjectOrder()
    {
        if (!OrderProjectsByLastActive) SavePreference(state => AppController.Profile(state, ProfileId).OrderProjectsByLastActive = true);
    }
    public void MoveProject(SessionTreeNode source, SessionTreeNode target, bool after)
    {
        source = _orderedProjects.FirstOrDefault(node => node.Key == source.Key) ?? source;
        target = _orderedProjects.FirstOrDefault(node => node.Key == target.Key) ?? target;
        if (!source.IsProject || !target.IsProject || source.IsFolderGroup || target.IsFolderGroup || source.ProfileId != ProfileId || target.ProfileId != ProfileId || source == target) return;
        var order = _orderedProjects.ToList();
        if (!order.Contains(target) || !order.Remove(source)) return;
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        SavePreference(state =>
        {
            var profile = AppController.Profile(state, ProfileId);
            profile.CustomProjectOrder = order.Select(node => node.Key).ToList();
            profile.OrderProjectsByLastActive = false;
        });
    }
    public IEnumerable<ProjectState> Projects => Profile.Projects.Where(project => !project.IsFolderGroup);
    public ProjectState? SelectedProject
    {
        get => Profile.Projects.FirstOrDefault(project => project.Id == _projectId);
        set { if (value is null || value.Id == _projectId) return; SelectProject(value.Id); LoadSelection(); }
    }
    public IReadOnlyList<SessionListItem> Chats => _sessionRows;
    public SessionListItem? SelectedSession
    {
        get => _chatId is null ? null : _sessionRows.FirstOrDefault(row => row.ProfileId == ProfileId && row.ProjectId == _projectId && row.ChatId == _chatId);
        set
        {
            if (value is null || value.ProfileId != ProfileId || value == SelectedSession) return;
            if (value.CanManage) SelectProjectChat(value.ProjectId!.Value, value.ChatId!.Value);
            else { SessionRequested?.Invoke(value); RefreshTree(); Changed(nameof(SelectedSession)); }
        }
    }
    public ChatState? SelectedChat
    {
        get => SelectedProject?.Chats.FirstOrDefault(chat => chat.Id == _chatId);
        set { if (value is null || value.Id == _chatId) return; SelectChat(value.Id); }
    }
    public bool HasProject => SelectedProject is not null;
    public bool HasChat => SelectedChat is not null;
    public bool CanCompose => SelectedProject is { } project && !_controller.IsDeletingProject(ProfileId, project.Id) && !_controller.IsArchivingProject(ProfileId, project.Id)
        && SelectedChat is not { IsArchived: true } && !Runtime.IsSessionArchiving(SelectedChat?.Id, SelectedChat?.CodexThreadId)
        && !Runtime.IsSessionDeleting(SelectedChat?.Id, SelectedChat?.CodexThreadId);
    public string EmptyAction => HasProject ? "+ New chat" : "+ New project";
    public string Heading => SelectedChat?.Name ?? (HasProject ? ProjectName : "Welcome to Mini Codex");
    public string ProjectName => SelectedProject?.Name ?? "No project selected";
    public string FolderSummary => SelectedProject?.FolderSummary ?? "Add a project folder to begin";
    public string FolderTooltip => SelectedProject?.FolderTooltip ?? "Create a project and attach its folders.";
    public string PrimaryFolder => SelectedChat?.WorkingDirectory ?? SelectedProject?.PrimaryFolder ?? "No primary folder";
    public string EmptyTitle => !Runtime.SignedIn ? "Sign in to Codex" : HasChat ? "What would you like to work on?" : HasProject ? "Your project is ready" : "Your folders, together";
    public string EmptyDescription => !Runtime.SignedIn ? "Choose Sign in below your account name. Each account profile has its own Codex sign-in."
        : HasChat ? "Every chat can work across all attached project folders. Only use this conversation in one client at a time."
        : HasProject ? "Every chat in this project uses all attached folders. Create a chat to send your next request."
        : "Create a project, add one or more folders, and choose a primary folder for commands.";
    public string ArchiveLabel => _showArchived ? "Show active chats" : "Archived chats";
    public bool ShowArchived => _showArchived;
    public bool CanDeleteArchivedChats => _showArchived && !_controller.IsDeletingArchivedChats(ProfileId);
    public string Search { get => _search; set { _search = value; Changed(nameof(Search), nameof(IsSearchVisible)); RefreshSessionRows(); } }
    public bool IsSearchVisible => _searchExpanded || !string.IsNullOrEmpty(_search);
    public void ExpandSearch() { _searchExpanded = true; Changed(nameof(IsSearchVisible)); }
    public void CollapseSearch() { _searchExpanded = false; Changed(nameof(IsSearchVisible)); }
    public string Draft
    {
        get => _chatId is { } id && _drafts.TryGetValue(id, out var text) ? text : "";
        set
        {
            if (value == Draft) return;
            var created = value.Length > 0 && CreateDraftChat();
            if (_chatId is { } id)
            {
                _drafts[id] = value;
                Runtime.Touch(); _draftStore.Touch(ProfileId, id);
            }
            Changed(nameof(Draft), nameof(ReadyToSend), nameof(DraftReferences), nameof(IsResumeAction), nameof(SendLabel));
            if (created) { Refresh(); ExpandSelectedProject(); }
        }
    }
    public IReadOnlyList<ComposerReference> DraftReferences => _chatId is { } id && _draftReferences.TryGetValue(id, out var references) ? references.Where(reference => reference.IsInText(Draft)).ToArray() : [];
    public void ClearDraft()
    {
        if (_chatId is { } id) ClearDraft(id, DraftRevision);
    }
    public long DraftRevision => _chatId is { } id ? _draftVersions.GetValueOrDefault(id) : 0;
    public ObservableCollection<ChatAttachment> DraftAttachments
    {
        get { if (_chatId is not { } id) return _emptyAttachments; if (!_attachments.TryGetValue(id, out var items)) _attachments[id] = items = []; return items; }
    }
    public ObservableCollection<ChatAnnotation> DraftAnnotations
    {
        get { if (_chatId is not { } id) return _emptyAnnotations; if (!_annotations.TryGetValue(id, out var items)) _annotations[id] = items = []; return items; }
    }
    public void ClearDraft(Guid id, long revision)
    {
        if (_draftVersions.GetValueOrDefault(id) != revision) return;
        _drafts.Remove(id); _draftReferences.Remove(id); _attachments.Remove(id); _annotations.Remove(id);
        _draftStore.Touch(ProfileId, id);
        if (_chatId == id) Changed(nameof(Draft), nameof(DraftReferences), nameof(DraftAttachments), nameof(DraftAnnotations), nameof(ReadyToSend), nameof(IsResumeAction), nameof(SendLabel));
    }
    private void ExtrasChanged()
    {
        if (_chatId is { } id) { Runtime.Touch(); _draftStore.Touch(ProfileId, id); }
        Changed(nameof(DraftAttachments), nameof(DraftAnnotations), nameof(ReadyToSend), nameof(IsResumeAction), nameof(SendLabel));
    }
    public void AddAttachment(ChatAttachment attachment)
    {
        EnsureDraftChat();
        if (!HasChat) return;
        if (DraftAttachments.Count >= 20) throw new InvalidOperationException("Attach up to 20 files and images per message.");
        if (!DraftAttachments.Any(item => FolderPaths.Comparer.Equals(item.Path, attachment.Path))) DraftAttachments.Add(attachment);
        ExtrasChanged();
    }
    public void RemoveAttachment(ChatAttachment attachment) { DraftAttachments.Remove(attachment); ExtrasChanged(); }
    public void AddAnnotation(string quote, string? imagePath = null)
    {
        if (!HasChat || string.IsNullOrWhiteSpace(quote)) return;
        if (quote.Length > 16 * 1024 || DraftAnnotations.Count >= 8 || DraftAnnotations.Sum(item => item.Quote.Length) + quote.Length > 64 * 1024)
            throw new InvalidOperationException("Use up to 8 quotes, with 16,384 characters per quote and 65,536 characters total.");
        ChatAttachment? attachment = null;
        if (imagePath is not null && !DraftAttachments.Any(item => FolderPaths.Comparer.Equals(item.Path, imagePath)))
        {
            if (DraftAttachments.Count >= 20) throw new InvalidOperationException("Attach up to 20 files and images per message.");
            attachment = AttachmentStore.FromFile(imagePath);
        }
        var annotation = new ChatAnnotation(quote); _draftStore.WatchAnnotation(ProfileId, _chatId!.Value, annotation);
        if (attachment is not null) DraftAttachments.Add(attachment);
        DraftAnnotations.Add(annotation); ExtrasChanged();
    }
    public void RemoveAnnotation(ChatAnnotation annotation) { DraftAnnotations.Remove(annotation); ExtrasChanged(); }
    public void TrackReference(ComposerReference reference)
    {
        if (_chatId is not { } id || !reference.IsInText(Draft)) return;
        if (!_draftReferences.TryGetValue(id, out var references)) _draftReferences[id] = references = [];
        if (!references.Any(item => item.Kind == reference.Kind && item.Path == reference.Path)) references.Add(reference);
        _draftStore.Touch(ProfileId, id);
        Changed(nameof(DraftReferences));
    }
    public string Effort
    {
        get => Efforts.FirstOrDefault(value => value.Equals(Profile.Effort, StringComparison.OrdinalIgnoreCase)) ?? SelectedModel?.DefaultEffort ?? "medium";
        set { if (value is null || value == Profile.Effort) return; SavePreference(state => AppController.Profile(state, ProfileId).Effort = value); }
    }
    public string ApprovalMode
    {
        get => Profile.ApprovalMode;
        set { if (value is null || value == Profile.ApprovalMode) return; SavePreference(state => AppController.Profile(state, ProfileId).ApprovalMode = value); }
    }
    public bool FastMode
    {
        get => Profile.FastMode;
        set { if (value == Profile.FastMode) return; SavePreference(state => AppController.Profile(state, ProfileId).FastMode = value); }
    }
    public string[] BusySendModes { get; } = ["Steer", "Queue", "Ask"];
    public string BusySendMode
    {
        get => Profile.BusySendMode;
        set { if (value is null || value == Profile.BusySendMode) return; SavePreference(state => AppController.Profile(state, ProfileId).BusySendMode = value); }
    }
    public bool IsResumeAction => string.IsNullOrWhiteSpace(Draft) && DraftAttachments.Count == 0 && DraftAnnotations.Count == 0
        && Conversation is { Busy: false } state && (state.ConflictPause is not null || state.LastTurnStatus == "interrupted");
    public string SendLabel => IsResumeAction ? "Resume" : Conversation?.Busy == true ? BusySendMode == "Ask" ? "Send…" : BusySendMode : "Send";
    public string QueueLabel => $"Queue ({Conversation?.Queue.Count ?? 0})";
    public string SpeedTooltip => FastMode
        ? "Fast selected for new messages. Click for Standard. Fast uses more of your limits or credits; availability depends on the model and account."
        : "Standard selected for new messages. Click for Fast. Fast uses more of your limits or credits; availability depends on the model and account.";
    public IReadOnlyList<string> Efforts => SelectedModel?.Efforts ?? [];
    public string[] ApprovalModes { get; } = ["Ask me", "Auto approval"];
    public CodexModel? SelectedModel
    {
        get => Runtime.Models.FirstOrDefault(model => model.Id == Profile.SelectedModel) ?? Runtime.Models.FirstOrDefault(model => model.IsDefault) ?? Runtime.Models.FirstOrDefault();
        set
        {
            if (value is null || value.Id == SelectedModel?.Id) return;
            SavePreference(state => AppController.Profile(state, ProfileId).SelectedModel = value.Id);
            Changed(nameof(SelectedModel), nameof(Efforts), nameof(Effort), nameof(ReadyToSend), nameof(CanPredict));
        }
    }
    public ConversationState? Conversation => SelectedChat is { } chat ? Runtime.Conversation(chat) : null;
    public ObservableCollection<ChatMessage> Messages => Conversation?.Messages ?? _emptyMessages;
    public ObservableCollection<TranscriptEntry> DisplayMessages => Conversation?.Transcript ?? _emptyTranscript;
    public IReadOnlyList<ComposerReference> MentionEntries
    {
        get
        {
            var discovered = Explorer?.Entries;
            var recovered = _chatId is { } id && _draftReferences.TryGetValue(id, out var references) ? references : [];
            if (_mentionVersion != Runtime.MentionVersion || !ReferenceEquals(discovered, _explorerMentions) || _mentionDraftChat != _chatId || _mentionDraftReferenceCount != recovered.Count)
            {
                _mentionVersion = Runtime.MentionVersion; _explorerMentions = discovered; _mentionDraftChat = _chatId; _mentionDraftReferenceCount = recovered.Count;
                _mentionCache = (discovered ?? []).Concat(Runtime.MentionEntries).Concat(recovered).DistinctBy(entry => (entry.Kind, entry.Path)).ToArray();
            }
            return _mentionCache;
        }
    }
    public bool HasMessages => Messages.Count > 0;
    public bool ShowEmpty => !HasMessages;
    public bool ReadyToSend => CanCompose && !Runtime.InstallingExtensions && Runtime.SignedIn && (Runtime.Connected || Runtime.Suspended) && SelectedModel is not null && SelectedChat is { IsArchived: false }
        && Conversation is { PausingForConflict: false, ContinuingConflict: false, Uncertain: false, Loading: false, Steering: false, QueueDispatching: false } state
        && (!state.Busy || BusySendMode != "Steer" || state.TurnId is not null && !state.IsCompacting)
        && (IsResumeAction
            ? !state.QueueEditing && (state.ConflictPause is null || Runtime.CanContinueConflict(state))
            : state.ConflictPause is null && (!string.IsNullOrWhiteSpace(Draft) || DraftAttachments.Count > 0 || DraftAnnotations.Count > 0));
    public bool CanReview => !Runtime.InstallingExtensions && Runtime.SignedIn && (Runtime.Connected || Runtime.Suspended) && HasProject && SelectedModel is not null && SelectedChat is not { IsArchived: true }
        && !HasConflictPause && Conversation is not { ContinuingConflict: true } && Conversation is not { Busy: true } && Conversation is not { Loading: true } && Conversation is not { Uncertain: true } && Conversation is not { QueueDispatching: true };
    public bool CanEditMessages => CanCompose && !Runtime.InstallingExtensions && Runtime.SignedIn && (Runtime.Connected || Runtime.Suspended) && SelectedModel is not null
        && Conversation is { ThreadId: not null, Loaded: true, UsesTurnHistory: false, Busy: false, Loading: false, Uncertain: false, Steering: false,
            QueueDispatching: false, QueueEditing: false, ConflictPause: null, PausingForConflict: false, ContinuingConflict: false };
    public bool CanPredict => CanCompose && !Runtime.InstallingExtensions && Runtime.SignedIn && (Runtime.Connected || Runtime.Suspended)
        && SelectedModel is not null && SelectedChat is { IsArchived: false } && Conversation is { Loading: false, Uncertain: false }
        && Messages.Any(message => message.Role is "You" or "Codex" or "Questions");
    public bool CanStop => Conversation is { Busy: true, Uncertain: false, TurnId: not null };
    public bool CanCompact => !Runtime.InstallingExtensions && Runtime.SignedIn && Runtime.Connected && SelectedChat is { IsArchived: false } && Conversation is { ConflictPause: null, ContinuingConflict: false, ThreadId: not null, Busy: false, Uncertain: false, Loading: false };
    public bool HasConflictPause => Conversation?.ConflictPause is not null;
    public string ConflictPauseNotice => Conversation?.ConflictNotice ?? "";
    public string ConflictPauseTooltip => Conversation?.ConflictTooltip ?? "";
    public bool CanContinueTask => !Runtime.InstallingExtensions && Runtime.SignedIn && (Runtime.Connected || Runtime.Suspended)
        && SelectedChat is { IsArchived: false } && SelectedModel is not null && Conversation is { } state && Runtime.CanContinueConflict(state);
    public bool CanCancelPausedTask => Conversation is { ConflictPause: not null, Busy: false, Uncertain: false, PausingForConflict: false, ContinuingConflict: false, QueueDispatching: false };
    public bool CanConfigure => Conversation is not { Busy: true } && Conversation is not { Uncertain: true };
    public bool HasApprovals => Runtime.Approvals.Count > 0;
    public string ApprovalLabel => $"Review ({Runtime.Approvals.Count})";
    public bool CanLoadEarlier => Conversation is { Busy: false, Loading: false, HistoryCursor: not null };
    public bool CanLoadMore => _sessions.HasMore(ProfileId, _showArchived);
    public bool CanRefreshSessions => !_sessions.IsLoading(ProfileId, _showArchived);
    public string SessionListStatus => $"{_orderedProjects.Count} {(_orderedProjects.Count == 1 ? "project" : "projects")} · {_sessions.Status(ProfileId, _showArchived)}";
    public string SessionListDetails => _sessions.Details(ProfileId, _showArchived);
    public bool IsCompacting => Runtime.Connected && Conversation is { IsCompacting: true, Busy: true, Uncertain: false };
    public string ContextUsed => Conversation?.ContextUsedText ?? "?";
    public string ContextCapacity => Conversation?.ContextCapacityText ?? "?";
    public string Context => IsCompacting ? $"Context used: compacting / {ContextCapacity}" : Conversation?.Context ?? "Context used ? / ?";
    public string ContextTooltip => IsCompacting ? "Compacting context. Used tokens update when Codex reports the new usage."
        : Conversation?.ContextTooltip ?? "Used tokens / total context capacity. Waiting for Codex to report context usage.";
    public string ChatStatus => Conversation?.Loading == true ? "Loading history" : Conversation is { } state ? Runtime.TaskActivity(state) : "Ready";
    public string TranscriptNotice => Conversation?.HasLargeTranscript == true ? "Large chat history loaded. You can keep loading earlier messages; memory use and rendering time may increase." : "";
    public string SessionNotice => !string.IsNullOrEmpty(_draftStore.Error) ? _draftStore.Error : string.IsNullOrEmpty(SaveNotice) ? Runtime.Notice : SaveNotice;

    public AccountViewModel(AppController controller, Guid profileId, AccountRuntime runtime, SessionCatalog sessions, DraftStore draftStore)
    {
        _controller = controller; ProfileId = profileId; Runtime = runtime; _sessions = sessions; _draftStore = draftStore;
        var saved = draftStore.For(profileId);
        _drafts = saved.Text; _draftReferences = saved.References; _attachments = saved.Attachments; _annotations = saved.Annotations; _draftVersions = saved.Versions;
        _draftStore.Changed += DraftStoreChanged;
        _controller.Changed += OnStateChanged;
        _sessions.Changed += OnSessionsChanged;
        Runtime.Updated += OnRuntimeUpdated;
        Runtime.SessionListChanged += OnSessionListChanged;
        Refresh();
        Explorer = new ProjectExplorerViewModel(this);
        Explorer.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProjectExplorerViewModel.Entries)) Changed(nameof(MentionEntries)); };
    }
    private void DraftStoreChanged(Guid profile, Guid? chat)
    {
        if (_disposed || profile != Guid.Empty && profile != ProfileId) return;
        foreach (var node in _treeNodes.Values)
            if (node.Session is { } session && (chat is null || session.ChatId == chat))
                node.UpdateDraft(session.ProfileId == ProfileId && HasDraft(session.ChatId));
        if (chat is null || chat == _chatId)
        {
            Changed(nameof(Draft), nameof(DraftReferences), nameof(DraftAttachments), nameof(DraftAnnotations), nameof(ReadyToSend), nameof(IsResumeAction), nameof(SendLabel));
            if (_mentionDraftChat != _chatId || _mentionDraftReferenceCount != (_chatId is { } id ? _draftReferences.GetValueOrDefault(id)?.Count ?? 0 : 0)) Changed(nameof(MentionEntries));
        }
        Changed(nameof(SessionNotice));
    }
    private bool HasDraft(Guid? chatId) => chatId is { } id
        && (!string.IsNullOrWhiteSpace(_drafts.GetValueOrDefault(id))
            || _attachments.GetValueOrDefault(id)?.Count > 0 || _annotations.GetValueOrDefault(id)?.Count > 0);

    public void SelectProject(Guid id) { if (_projectId == id && _chatId is null) return; _projectId = id; _chatId = null; Refresh(); }
    public void SelectProjectChat(Guid projectId, Guid chatId) { _projectId = projectId; _chatId = chatId; _showArchived = SelectedChat?.IsArchived ?? false; Refresh(); ExpandSelectedProject(); LoadSelection(); }
    public void SelectChat(Guid id) { _chatId = id; _showArchived = SelectedChat?.IsArchived ?? false; Refresh(); ExpandSelectedProject(); LoadSelection(); }
    public void NavigateChatHistory(bool forward)
    {
        if (_disposed) return;
        var direction = forward ? 1 : -1;
        var index = _chatHistoryIndex;
        // A project page has no chat, so Back first returns to the last visited chat.
        if (!forward && SelectedChat is null) index++;
        for (index += direction; index >= 0 && index < _chatHistory.Count; index += direction)
        {
            var chatId = _chatHistory[index];
            if (chatId == _chatId) continue;
            var project = Profile.Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == chatId));
            if (project is null || _controller.IsDeletingProject(ProfileId, project.Id)) continue;
            _chatHistoryIndex = index;
            SelectProjectChat(project.Id, chatId);
            return;
        }
    }
    private void RecordChatVisit()
    {
        if (SelectedChat is not { } chat || _chatHistoryIndex >= 0 && _chatHistory[_chatHistoryIndex] == chat.Id) return;
        var next = _chatHistoryIndex + 1;
        if (next < _chatHistory.Count) _chatHistory.RemoveRange(next, _chatHistory.Count - next);
        _chatHistory.Add(chat.Id);
        _chatHistoryIndex = _chatHistory.Count - 1;
    }
    public void ActivateTreeNode(SessionTreeNode node)
    {
        if (IsUpdatingTree || node.ProfileId != ProfileId || node.IsLoadMore || node.IsSection) return;
        if (node.Session is { } session) SelectedSession = session;
        else if (node.IsProject) SelectProject(ProjectForNode(node));
    }
    public IReadOnlyList<string> FoldersForNode(SessionTreeNode node)
    {
        if (!node.IsProject || node.ProfileId != ProfileId) return [];
        IReadOnlyList<string>? folders = Profile.Projects.FirstOrDefault(project => project.Id == node.ProjectId)?.Folders;
        folders ??= _sessions.Projects(ProfileId).FirstOrDefault(project => project.Id == node.RemoteProjectId)?.Folders;
        folders ??= _sessionRows.FirstOrDefault(session => StringComparer.OrdinalIgnoreCase.Equals(SessionProjectKey(session), node.Key))?.Folders;
        if (folders is null or { Count: 0 }) folders = node.FolderPath is { Length: > 0 } folder ? [folder] : [];
        return folders.Where(folder => Path.IsPathFullyQualified(folder)).Select(FolderPaths.Normalize).Distinct(FolderPaths.Comparer).ToArray();
    }
    public Guid ProjectForNode(SessionTreeNode node)
    {
        if (!node.IsProject || node.ProfileId != ProfileId) throw new InvalidOperationException("Choose a project or chat folder.");
        if (node.ProjectId is { } existing)
        { _controller.EnsureProjectAvailable(ProfileId, existing); return existing; }
        if (node.RemoteProjectId is null && node.FolderPath is { Length: > 0 } folder) return _controller.FolderGroup(ProfileId, folder);
        if (node.RemoteProjectId is { } remote)
        {
            var project = _sessions.Projects(ProfileId).FirstOrDefault(project => project.Id == remote);
            if (project is null && node.Children.FirstOrDefault()?.Session is { } chat)
                project = new CodexProject(remote, node.Name, chat.Folders, 0);
            if (project is not null) return _controller.RegisterProject(ProfileId, project);
        }
        throw new InvalidOperationException("This project has no available folders. Refresh the session list.");
    }
    public void NewTreeChat(SessionTreeNode node)
    {
        var projectId = ProjectForNode(node);
        SelectProjectChat(projectId, _controller.AddChat(ProfileId, projectId));
    }
    public void NewFolderChat(string folder)
    {
        var projectId = _controller.FolderGroup(ProfileId, folder);
        SelectProjectChat(projectId, _controller.AddChat(ProfileId, projectId));
    }
    public void LoadMoreProjectSessions(SessionTreeNode node)
    {
        if (node.LoadMoreProject is not { } project || project.ProfileId != ProfileId
            || !_treeNodes.TryGetValue(project.Key, out var current) || !ReferenceEquals(project, current)) return;
        project.ShowMoreSessions();
        RefreshTree();
    }
    public void SelectRemoteChat(string? threadId)
    {
        foreach (var project in Profile.Projects)
            if (project.Chats.FirstOrDefault(chat => chat.CodexThreadId == threadId) is { } chat)
            { _projectId = project.Id; SelectChat(chat.Id); return; }
    }
    public void ToggleArchived() { _showArchived = !_showArchived; _chatId = null; Refresh(); EnsureSessionList(); }
    public async Task RefreshSessionsAsync(bool more = false)
    {
        if (_disposed) return;
        await _sessions.RefreshAsync(ProfileId, _showArchived, more);
        if (!_disposed) await LoadSelectionAsync();
    }
    public async Task EnsureSessionsAsync()
    {
        if (_disposed) return;
        await LoadSelectionAsync();
        if (!_disposed) await _sessions.EnsureLoadedAsync(ProfileId, _showArchived);
    }
    private async void EnsureSessionList()
    { try { await EnsureSessionsAsync(); } catch (Exception error) { Runtime.Report(error); } }
    public async Task LoadSelectionAsync()
    {
        if (!_disposed && Runtime.SignedIn && SelectedProject is { } project && SelectedChat is { } chat)
            await Runtime.LoadChatAsync(project, chat);
    }
    private async void LoadSelection()
    { try { await LoadSelectionAsync(); } catch (Exception error) { Runtime.Report(error); } }
    private void OnRuntimeUpdated(object? sender, EventArgs e)
    {
        foreach (var node in _treeNodes.Values.Where(node => node.Session is not null))
            node.UpdateActivity(Runtime.IsSessionRunning(node.Session!.ChatId, node.Session.ThreadId), Runtime.QueueCountFor(node.Session.ChatId, node.Session.ThreadId), Runtime.SessionActivity(node.Session.ChatId, node.Session.ThreadId), Runtime.IsSessionArchiving(node.Session.ChatId, node.Session.ThreadId), Runtime.IsSessionDeleting(node.Session.ChatId, node.Session.ThreadId));
        Changed(nameof(MentionEntries), nameof(DisplayMessages), nameof(CanCompose));
        NotifyLive();
        var signedInNow = Runtime.SignedIn && !_wasSignedIn;
        _wasSignedIn = Runtime.SignedIn;
        if (signedInNow) EnsureSessionList();
    }
    private async void OnSessionListChanged(string? createdThread)
    {
        if (_disposed) return;
        try { await _sessions.RefreshForChangeAsync(ProfileId, createdThread); }
        catch (Exception error) { if (!_disposed) Runtime.Report(error); }
    }
    private void NotifyLive() => Changed(nameof(SelectedModel), nameof(Efforts), nameof(Effort), nameof(Messages), nameof(HasMessages), nameof(ShowEmpty), nameof(ReadyToSend), nameof(CanPredict), nameof(CanReview), nameof(CanEditMessages), nameof(IsResumeAction), nameof(SendLabel), nameof(QueueLabel), nameof(CanStop), nameof(CanCompact), nameof(CanConfigure), nameof(HasConflictPause), nameof(ConflictPauseNotice), nameof(ConflictPauseTooltip), nameof(CanContinueTask), nameof(CanCancelPausedTask), nameof(HasApprovals), nameof(ApprovalLabel), nameof(CanLoadEarlier), nameof(CanLoadMore), nameof(IsCompacting), nameof(ContextUsed), nameof(ContextCapacity), nameof(Context), nameof(ContextTooltip), nameof(ChatStatus), nameof(TranscriptNotice), nameof(SessionNotice), nameof(EmptyTitle), nameof(EmptyDescription));
    private void OnStateChanged(object? sender, EventArgs e) { if (!_creatingChat) Refresh(); }
    private bool CreateDraftChat()
    {
        if (HasChat || !HasProject) return false;
        _creatingChat = true;
        try { _chatId = _controller.AddChat(ProfileId, SelectedProject!.Id); return true; }
        finally { _creatingChat = false; }
    }
    public void EnsureDraftChat() { if (CreateDraftChat()) { Refresh(); ExpandSelectedProject(); } }
    private void OnSessionsChanged(object? sender, EventArgs e) => RefreshSessionRows();
    private void RefreshSessionRows()
    {
        _sessionRows = _sessions.Rows(ProfileId, _showArchived, _search);
        RefreshTree();
        Changed(nameof(Chats), nameof(SelectedSession), nameof(CanLoadMore), nameof(CanRefreshSessions), nameof(SessionListStatus), nameof(SessionListDetails));
    }
    private static string ProjectKey(Guid profileId, Guid? projectId, string folder, string? remoteId = null) => profileId.ToString("N") + "/project/" + (remoteId is not null ? "codex/" + remoteId : projectId?.ToString("N") ?? "folder/" + FolderPaths.Normalize(folder));
    private string LocalProjectKey(ProjectState project) => ProjectKey(ProfileId, project.IsFolderGroup ? null : project.Id, project.PrimaryFolder, project.CodexProjectId);
    private static string SessionProjectKey(SessionListItem session) => ProjectKey(session.ProfileId, session.IsUnassigned ? null : session.ProjectId, session.PrimaryFolder, session.CodexProjectId);
    private SessionTreeNode TreeNode(string key, bool project)
    {
        if (!_treeNodes.TryGetValue(key, out var node)) _treeNodes[key] = node = new SessionTreeNode(key, project);
        return node;
    }
    private void RefreshTree()
    {
        IsUpdatingTree = true;
        try
        {
            var roots = new Dictionary<string, SessionTreeNode>(StringComparer.OrdinalIgnoreCase);
            var children = new Dictionary<string, List<SessionTreeNode>>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in Profile.Projects)
            {
                var key = LocalProjectKey(project);
                var root = TreeNode(key, true);
                root.RemoteProjectId = project.CodexProjectId;
                root.UpdateProject(ProfileId, project.Id, project.Name, project.FolderTooltip, true, 0, project.IsFolderGroup, project.PrimaryFolder,
                    _controller.IsDeletingProject(ProfileId, project.Id), _controller.IsArchivingProject(ProfileId, project.Id), project.IsArchived);
                roots[key] = root; children[key] = [];
            }
            foreach (var remote in _sessions.Projects(ProfileId))
            {
                var local = Profile.Projects.FirstOrDefault(project => project.CodexProjectId == remote.Id);
                var key = ProjectKey(ProfileId, local?.Id, remote.Folders.FirstOrDefault() ?? "", remote.Id);
                var root = TreeNode(key, true); root.RemoteProjectId = remote.Id;
                root.UpdateProject(ProfileId, local?.Id, local?.Name ?? remote.Name, local?.FolderTooltip ?? string.Join("\n", remote.Folders), local is not null, 0,
                    isFolderGroup: local?.IsFolderGroup ?? (remote.Folders.Count > 0 && remote.Folders.All(FolderPaths.IsDefaultChatFolder)),
                    primaryFolder: local?.PrimaryFolder ?? remote.Folders.FirstOrDefault(), isDeleting: _controller.IsDeletingArchivedChats(ProfileId) || local is not null && _controller.IsDeletingProject(ProfileId, local.Id),
                    isArchiving: local is not null && _controller.IsArchivingProject(ProfileId, local.Id), isArchived: local?.IsArchived ?? false);
                roots[key] = root; children[key] = [];
            }
            foreach (var session in _sessionRows)
            {
                var key = SessionProjectKey(session);
                if (!roots.TryGetValue(key, out var root))
                {
                    root = TreeNode(key, true);
                    root.RemoteProjectId = session.CodexProjectId;
                    root.UpdateProject(ProfileId, session.ProjectId, session.ProjectName, session.PrimaryFolder, false, 0, session.IsUnassigned, session.PrimaryFolder,
                        _controller.IsDeletingArchivedChats(ProfileId) || session.ProjectId is { } deletingId && _controller.IsDeletingProject(ProfileId, deletingId),
                        session.ProjectId is { } archivingId && _controller.IsArchivingProject(ProfileId, archivingId),
                        Profile.Projects.FirstOrDefault(project => project.Id == session.ProjectId)?.IsArchived ?? false);
                    roots[key] = root; children[key] = [];
                }
                var leaf = TreeNode("session/" + session.Key, false); leaf.UpdateSession(session);
                leaf.UpdateDraft(session.ProfileId == ProfileId && HasDraft(session.ChatId));
                leaf.UpdateActivity(Runtime.IsSessionRunning(session.ChatId, session.ThreadId), Runtime.QueueCountFor(session.ChatId, session.ThreadId), Runtime.SessionActivity(session.ChatId, session.ThreadId), Runtime.IsSessionArchiving(session.ChatId, session.ThreadId), Runtime.IsSessionDeleting(session.ChatId, session.ThreadId));
                children[key].Add(leaf);
            }
            foreach (var key in roots.Keys.Where(key => _showArchived ? children[key].Count == 0 && !roots[key].IsArchived : roots[key].IsArchived).ToArray())
            { roots.Remove(key); children.Remove(key); }
            foreach (var (key, root) in roots)
            {
                var sessions = children[key];
                var visibleSessions = sessions.Take(root.VisibleSessionCount).ToList();
                if (sessions.Count > visibleSessions.Count)
                {
                    var more = TreeNode(key + "/more", false);
                    more.UpdateLoadMore(root, sessions.Count - visibleSessions.Count);
                    visibleSessions.Add(more);
                }
                SyncNodes(root.Children, visibleSessions);
                root.UpdateProject(ProfileId, root.ProjectId, root.Name, root.Tooltip, root.CanManageProject, sessions.Count, root.IsFolderGroup, root.FolderPath, root.IsDeleting, root.IsArchiving, root.IsArchived);
                if (!string.IsNullOrEmpty(_search) && root.Children.Count > 0) root.IsExpanded = true;
            }
            // Saved history provides a baseline until this app records a task start. Drafts and
            // later history updates must not move a project ahead of a more recently started task.
            var activity = _sessions.Rows(ProfileId, false, "").Concat(_sessions.Rows(ProfileId, true, ""))
                .Where(row => row.ThreadId is not null)
                .GroupBy(SessionProjectKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(row => row.UpdatedAt), StringComparer.OrdinalIgnoreCase);
            DateTimeOffset LastActive(SessionTreeNode root)
            {
                var startedAt = Profile.Projects.FirstOrDefault(project => project.Id == root.ProjectId)?.LastActiveAt ?? default;
                return startedAt != default ? startedAt : activity.GetValueOrDefault(root.Key);
            }
            int CustomPosition(SessionTreeNode root)
            {
                var position = Profile.CustomProjectOrder.FindIndex(key => key.Equals(root.Key, StringComparison.OrdinalIgnoreCase)
                    || root.ProjectId is { } id && key.Equals(ProjectKey(ProfileId, id, ""), StringComparison.OrdinalIgnoreCase)
                    || key.Equals(ProjectKey(ProfileId, null, Profile.Projects.FirstOrDefault(project => project.Id == root.ProjectId)?.PrimaryFolder
                        ?? root.Children.FirstOrDefault()?.Session?.PrimaryFolder ?? root.Tooltip), StringComparison.OrdinalIgnoreCase));
                return position < 0 ? int.MaxValue : position;
            }
            _orderedProjects = (OrderProjectsByLastActive
                ? roots.Values.Where(root => !root.IsFolderGroup).OrderByDescending(LastActive)
                : roots.Values.Where(root => !root.IsFolderGroup).OrderBy(CustomPosition)).ThenBy(root => root.Name, StringComparer.OrdinalIgnoreCase).ThenBy(root => root.Key, StringComparer.OrdinalIgnoreCase).ToArray();
            bool Matches(SessionTreeNode root) => string.IsNullOrEmpty(_search) || root.Children.Count > 0 || root.Name.Contains(_search, StringComparison.OrdinalIgnoreCase);
            var otherChats = TreeNode(ProfileId.ToString("N") + "/other-chats", false);
            otherChats.UpdateSection(ProfileId);
            SyncNodes(otherChats.Children, roots.Values.Where(root => root.IsFolderGroup && Matches(root)).OrderByDescending(LastActive)
                .ThenBy(root => root.Name, StringComparer.OrdinalIgnoreCase).ToArray());
            if (!string.IsNullOrEmpty(_search) && otherChats.Children.Count > 0) otherChats.IsExpanded = true;
            var visibleProjects = _orderedProjects.Where(Matches);
            if (!_showArchived || otherChats.Children.Count > 0) visibleProjects = visibleProjects.Append(otherChats);
            SyncNodes(SessionProjects, visibleProjects.ToArray());
            var retained = roots.Keys.Concat(children.Values.SelectMany(nodes => nodes).Select(node => node.Key))
                .Concat(roots.Values.SelectMany(root => root.Children).Select(node => node.Key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            retained.Add(otherChats.Key);
            foreach (var key in _treeNodes.Keys.Where(key => !retained.Contains(key)).ToArray()) _treeNodes.Remove(key);
            foreach (var root in roots.Values)
            {
                root.IsSelected = _chatId is null && _projectId is not null && root.ProfileId == ProfileId && root.ProjectId == _projectId;
                foreach (var leaf in root.Children)
                    leaf.IsSelected = leaf.Session is { } session && session.ProfileId == ProfileId && session.ProjectId == _projectId && session.ChatId is not null && session.ChatId == _chatId;
            }
        }
        finally { IsUpdatingTree = false; }
    }
    private static void SyncNodes(ObservableCollection<SessionTreeNode> target, IReadOnlyList<SessionTreeNode> desired)
    {
        var keep = desired.ToHashSet();
        for (var index = target.Count - 1; index >= 0; index--) if (!keep.Contains(target[index])) target.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < target.Count && ReferenceEquals(target[index], desired[index])) continue;
            var previous = target.IndexOf(desired[index]);
            if (previous >= 0) target.Move(previous, index); else target.Insert(index, desired[index]);
        }
    }
    private void ExpandSelectedProject()
    {
        foreach (var project in _treeNodes.Values.Where(node => node.IsProject && node.ProfileId == ProfileId && node.ProjectId == _projectId)) project.IsExpanded = true;
        if (SelectedProject?.IsFolderGroup == true && _treeNodes.TryGetValue(ProfileId.ToString("N") + "/other-chats", out var otherChats)) otherChats.IsExpanded = true;
    }
    private void SavePreference(Action<AppState> change)
    {
        try { _controller.Commit(change); SaveNotice = ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { SaveNotice = error.Message; Changed(nameof(Effort), nameof(ApprovalMode), nameof(FastMode), nameof(SpeedTooltip), nameof(BusySendMode), nameof(OrderProjectsByLastActive)); }
        Changed(nameof(SaveNotice), nameof(SessionNotice));
    }
    private void Refresh()
    {
        if (_chatId is { } selected && SelectedChat is null && Profile.Projects.FirstOrDefault(project => project.Chats.Any(chat => chat.Id == selected)) is { } owner)
            _projectId = owner.Id;
        var chatIds = Profile.Projects.SelectMany(project => project.Chats).Select(chat => chat.Id).ToHashSet();
        foreach (var removed in _drafts.Keys.Where(id => !chatIds.Contains(id)).ToArray()) _drafts.Remove(removed);
        foreach (var removed in _draftReferences.Keys.Where(id => !chatIds.Contains(id)).ToArray()) _draftReferences.Remove(removed);
        foreach (var removed in _attachments.Keys.Where(id => !chatIds.Contains(id)).ToArray()) _attachments.Remove(removed);
        foreach (var removed in _annotations.Keys.Where(id => !chatIds.Contains(id)).ToArray()) _annotations.Remove(removed);
        foreach (var removed in _draftVersions.Keys.Where(id => !chatIds.Contains(id)).ToArray()) _draftVersions.Remove(removed);
        if (SelectedProject is null) _projectId = Profile.Projects.FirstOrDefault()?.Id;
        if (SelectedChat is null || SelectedChat.IsArchived != _showArchived) _chatId = null;
        RefreshSessionRows();
        Changed(nameof(ProfileName), nameof(Initials), nameof(Projects), nameof(SelectedProject), nameof(Chats), nameof(ArchiveLabel), nameof(ShowArchived), nameof(CanDeleteArchivedChats), nameof(Effort), nameof(ApprovalMode), nameof(FastMode), nameof(SpeedTooltip), nameof(BusySendMode), nameof(OrderProjectsByLastActive));
        NotifySelection();
    }
    private void NotifySelection()
    {
        RecordChatVisit();
        if (_pinnedChat != _chatId) { if (_pinnedChat is { } old) Runtime.UnpinConversation(old); _pinnedChat = _chatId; if (_pinnedChat is { } current) Runtime.PinConversation(current); }
        Changed(nameof(DisplayMessages), nameof(DraftAttachments), nameof(DraftAnnotations), nameof(MentionEntries));
        Changed(nameof(SelectedChat), nameof(HasProject), nameof(HasChat), nameof(CanCompose), nameof(EmptyAction), nameof(Heading), nameof(ProjectName), nameof(FolderSummary), nameof(FolderTooltip), nameof(PrimaryFolder), nameof(EmptyTitle), nameof(EmptyDescription), nameof(Draft), nameof(DraftReferences));
        NotifyLive();
    }
    public void Dispose() { _disposed = true; if (_pinnedChat is { } id) Runtime.UnpinConversation(id); Explorer.Dispose(); _draftStore.Changed -= DraftStoreChanged; _controller.Changed -= OnStateChanged; Runtime.Updated -= OnRuntimeUpdated; Runtime.SessionListChanged -= OnSessionListChanged; _sessions.Changed -= OnSessionsChanged; }
}
