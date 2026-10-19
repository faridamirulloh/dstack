using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using MiniCodex.Codex;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.ViewModels;

namespace MiniCodex.Views;

public partial class AccountPane : UserControl
{
    private readonly AppController _controller;
    private readonly WindowManager _windows;
    private readonly AccountViewModel _model;
    private AccountRuntime Runtime => _model.Runtime;
    private bool _disposed;
    private bool _loaded;
    private AccountWindow? _hostWindow;
    private readonly List<Window> _ownedWindows = [];
    private bool _closing;
    private Task<bool>? _closeTask;
    private bool _authBusy;
    private bool _sending;
    private (ProjectState Project, ChatState Chat)? _headerRenameTarget;
    public bool IsClosing => _closing;
    public Guid ProfileId => _model.ProfileId;
    public string ProfileName => _model.ProfileName;
    public AccountWindow HostWindow => _hostWindow ?? throw new InvalidOperationException("This account pane has no window.");
    public event EventHandler? Closed;
    public void SelectChat(Guid projectId, Guid chatId) => _model.SelectProjectChat(projectId, chatId);

    public AccountPane(AppController controller, WindowManager windows, Guid profileId)
    {
        _controller = controller; _windows = windows;
        _model = new AccountViewModel(controller, profileId, windows.Runtimes.Get(profileId), windows.Sessions, windows.Drafts);
        _model.SessionRequested += session => _ = RunAsync(() => _windows.OpenSessionAsync(session));
        InitializeComponent(); DataContext = _model;
        InitializeMentions();
        InitializeTranscript();
        InitializeFind();
        InitializeActivity();
        InitializePredictions();
        PreviewKeyDown += TerminalShortcut;
        PreviewKeyDown += (_, _) => WakeOnInput();
        PreviewKeyDown += ChatHistoryShortcut;
        PreviewMouseDown += (_, _) => WakeOnInput();
        PreviewMouseDown += ChatNameOutsideClick;
        PreviewMouseDown += ChatHistoryMouseButton;
        PreviewMouseUp += ChatHistoryMouseButton;
        PreviewMouseWheel += (_, _) => WakeOnInput();
        DataObject.AddPastingHandler(Composer, PasteAttachments);
        Loaded += async (_, _) =>
        {
            if (_loaded || _closing || _disposed) return;
            _loaded = true;
            try { await Runtime.ConnectAsync(); if (!_closing) await _model.EnsureSessionsAsync(); }
            catch (Exception error) { Runtime.Report(error); }
        };
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_closing) return;
        try { await action(); }
        catch (Exception error)
        {
            if (_closing || !IsLoaded) return;
            Runtime.Report(error);
            ShowMessage(error.Message, "Mini Codex", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Guard(Action action) => _ = RunAsync(() => { action(); return Task.CompletedTask; });

    private void ChatHistoryShortcut(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.Alt || key is not (Key.Left or Key.Right)) return;
        e.Handled = true;
        Guard(() => _model.NavigateChatHistory(key == Key.Right));
    }

    private void ChatHistoryMouseButton(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2)) return;
        e.Handled = true;
        if (e.ButtonState == MouseButtonState.Released)
            Guard(() => _model.NavigateChatHistory(e.ChangedButton == MouseButton.XButton2));
    }

    internal void AttachTo(AccountWindow window)
    {
        if (_hostWindow is not null) TrackHostActivity(_hostWindow, false);
        _hostWindow = window;
        TrackHostActivity(window, true);
        if (_explorerWindow is not null) _explorerWindow.Owner = window;
        foreach (var owned in _ownedWindows) owned.Owner = window;
        UpdateViewedConversation();
    }

    internal void SetSplitMode(bool split) => SplitActions.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
    internal void FocusComposer() => Composer.Focus();
    private void DetachPane(object sender, RoutedEventArgs e) => Guard(() => _windows.DetachPane(this));
    private async void ClosePane(object sender, RoutedEventArgs e) => await CloseAccountAsync();

    internal void DisposePane()
    {
        if (_disposed) return;
        _disposed = true; _closing = true;
        _explorerWindow?.Close();
        foreach (var owned in _ownedWindows.ToArray()) owned.Close();
        DisposePredictions(); DisposeActivity(); ClearProjectDropMarker(); DisposeMentions(); DisposeTranscript(); DisposeFind();
        DataObject.RemovePastingHandler(Composer, PasteAttachments); _model.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void FinishClose()
    {
        HostWindow.RemovePane(this);
        DisposePane();
    }

    private void ShowOwnedWindow(Window window)
    {
        PrepareDialog(window);
        _ownedWindows.Add(window);
        window.Closed += (_, _) => _ownedWindows.Remove(window);
        window.Show();
    }

    public Task<bool> CloseAccountAsync()
    {
        if (_closeTask is { IsCompleted: false }) return _closeTask;
        if (_closing) return Task.FromResult(false);
        return _closeTask = CloseAccountCoreAsync();
    }

    private async Task<bool> CloseAccountCoreAsync()
    {
        _closing = true;
        try
        {
            if (_windows.HasOtherAccountWindow(_model.ProfileId, this)) { FinishClose(); return true; }
            if (Runtime.InstallingExtensions) throw new InvalidOperationException("Wait for extension installation to finish before closing this account's last window.");
            if ((Runtime.HasWork || Runtime.HasQueuedMessages) && ShowMessage("Stop this account's tasks, pause its saved queues, and close this account pane? If the connection was lost, this also stops its Codex process. Cancel keeps the pane and tasks open.", "Close account", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            { _closing = false; return false; }
            IsEnabled = false;
            if (!Runtime.Connected && Runtime.HasWork) await Runtime.StopAfterLossAsync();
            else await Runtime.StopAllAsync();
            _windows.Drafts.Flush();
            await _windows.Runtimes.ReleaseAsync(_model.ProfileId);
            FinishClose();
            return true;
        }
        catch (Exception error)
        {
            _closing = false; IsEnabled = true; Runtime.Report(error);
            ShowMessage(error.Message + "\nThe account pane remains open.", "Could not stop Codex", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private async Task AuthAsync(Func<Task> action)
    {
        if (_authBusy) return;
        _authBusy = true; AuthPanel.IsEnabled = false;
        try { await RunAsync(action); }
        finally { _authBusy = false; AuthPanel.IsEnabled = true; }
    }
    private async void SignIn(object sender, RoutedEventArgs e) => await AuthAsync(() => Runtime.BeginLoginAsync(false));
    public Task SignInAsync() => AuthAsync(async () =>
    {
        await ConnectOrRecoverAsync();
        if (!Runtime.Connected) return;
        await Runtime.RefreshAccountAsync();
        if (Runtime.SignedIn) return;
        if (Runtime.LoginPending) Runtime.OpenLoginBrowser();
        else await Runtime.BeginLoginAsync(false);
    });
    private async void DeviceSignIn(object sender, RoutedEventArgs e) => await AuthAsync(() => Runtime.BeginLoginAsync(true));
    private async void CancelSignIn(object sender, RoutedEventArgs e) => await AuthAsync(Runtime.CancelLoginAsync);
    private async Task ConnectOrRecoverAsync()
    {
        if (!Runtime.Connected && Runtime.HasWork)
        {
            if (ShowMessage("The previous connection failed during a task. Stop the old Codex process and reconnect? Refresh chat afterward to recover its saved history. The task will not be resent.", "Recover connection", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            await Runtime.RestartAfterLossAsync();
        }
        else await Runtime.ConnectAsync();
    }
    private async void Reconnect(object sender, RoutedEventArgs e) => await AuthAsync(ConnectOrRecoverAsync);
    private async void RefreshUsage(object sender, RoutedEventArgs e) => await RunAsync(Runtime.RefreshUsageAsync);
    private async void SetupSandbox(object sender, RoutedEventArgs e) => await RunAsync(Runtime.SetupSandboxAsync);
    private void ChooseCodex(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var picker = new OpenFileDialog { Title = "Select official Codex 0.162.1 executable", Filter = "Codex executable (codex.exe)|codex.exe", CheckFileExists = true };
        if (picker.ShowDialog(HostWindow) != true) return;
        _controller.Commit(state => state.CodexExecutable = Path.GetFullPath(picker.FileName));
        ShowMessage("Saved. The version is checked when the next account connection starts. Keep Codex's companion files beside the executable.", "Codex executable");
    });
    private async void RefreshChat(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_model.SelectedProject is not { } project || _model.SelectedChat is not { } chat) return;
        await ConnectOrRecoverAsync();
        if (Runtime.Connected) await Runtime.ReconcileAsync(project, chat);
    });
    private async void LoadEarlier(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await LoadEarlierKeepingPositionAsync();
    });
    private async void RefreshChats(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await _model.RefreshSessionsAsync();
    });
    private async void MoreChats(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await _model.RefreshSessionsAsync(true);
    });
    private async void SendMessage(object sender, RoutedEventArgs e) => await SendAsync();
    private async void ComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleMentionKey(e)) return;
        if (Composer.HandleListKey(e.Key, Keyboard.Modifiers)) { e.Handled = true; return; }
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true; await SendAsync();
    }
    private async Task SendAsync()
    {
        if (_sending) return;
        _sending = true;
        try { await SendDraftAsync(); }
        finally { _sending = false; }
    }
    private async Task SendDraftAsync() => await RunAsync(async () =>
    {
        if (Runtime.Suspended || _waking) await Runtime.ConnectAsync();
        if (!_model.ReadyToSend || _model.SelectedProject is not { } project || _model.SelectedChat is not { } chat || _model.SelectedModel is not { } model) return;
        if (_model.IsResumeAction)
        {
            CloseMentions();
            ClosePredictions();
            await ResumeChatAsync(project, chat, model);
            return;
        }
        CancelHistoryRestore();
        var revision = _model.DraftRevision;
        var attachments = _model.DraftAttachments.ToArray();
        AttachmentStore.Validate(attachments);
        var annotations = _model.DraftAnnotations.Select(annotation => new ChatAnnotation(annotation.Quote, annotation.Comment)).ToArray();
        MessagePayload.ValidateAnnotations(annotations);
        var text = MessagePayload.Compose(_model.Draft, annotations, attachments);
        var references = _model.DraftReferences.ToArray();
        CloseMentions();
        ClosePredictions();
        var state = Runtime.Conversation(chat);
        var mode = state.Busy ? _model.BusySendMode : "Send";
        if (mode == "Ask")
        {
            var choice = PrepareDialog(new BusySendWindow(state.TurnId is not null && !state.IsCompacting));
            if (choice.ShowDialog() != true) return;
            mode = choice.Choice;
        }
        if (mode == "Steer") await Runtime.SteerAsync(project, chat, text, references, attachments);
        else if (mode == "Queue") Runtime.Enqueue(project, chat, new QueuedMessage { Text = text, ModelId = model.Id, ModelName = model.Name,
            Effort = _model.Effort, ApprovalMode = _model.ApprovalMode, FastMode = _model.FastMode, References = references,
            Attachments = attachments.Select(file => new AttachmentReference(file.Path, file.IsImage)).ToArray() });
        else
        {
            await Runtime.SendAsync(project, chat, text, model, _model.Effort, _model.ApprovalMode, references, attachments, _model.FastMode);
        }
        _model.ClearDraft(chat.Id, revision);
        if (_model.SelectedChat?.Id == chat.Id) { _followTail = true; ScrollToLatest(); }
    });
    private async void StopTask(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (_model.Conversation is { } chat) await Runtime.StopAsync(chat); });
    private async void ContinuePausedTask(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!_model.CanContinueTask || _model.SelectedProject is not { } project || _model.SelectedChat is not { } chat || _model.SelectedModel is not { } model) return;
        await ResumeChatAsync(project, chat, model);
    });
    private async Task ResumeChatAsync(ProjectState project, ChatState chat, CodexModel model)
    {
        CancelHistoryRestore();
        if (Runtime.Conversation(chat).ConflictPause is not null)
            await Runtime.ContinueConflictAsync(project, chat, model, _model.Effort, _model.ApprovalMode, _model.FastMode);
        else
            await Runtime.SendAsync(project, chat, "Continue the unfinished task from the interrupted turn. Reread the relevant files, preserve existing changes, and finish the remaining work without repeating completed steps.",
                model, _model.Effort, _model.ApprovalMode, fastMode: _model.FastMode);
        if (_model.SelectedChat?.Id == chat.Id) { _followTail = true; ScrollToLatest(); }
    }
    private void CancelPausedTask(object sender, RoutedEventArgs e) => Guard(() =>
    { if (_model.CanCancelPausedTask && _model.Conversation is { } state) Runtime.CancelConflict(state); });
    private async void CompactContext(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_model.CanCompact && _model.SelectedProject is { } project && _model.SelectedChat is { } chat)
            await Runtime.CompactAsync(project, chat, _model.ApprovalMode, _model.FastMode);
    });
    private bool _waking;
    private async void WakeOnInput()
    {
        Runtime.Touch();
        if (!Runtime.Suspended || _waking || _closing) return;
        _waking = true;
        try { await RunAsync(Runtime.ConnectAsync); } finally { _waking = false; }
    }
    private void OpenQueue(object sender, RoutedEventArgs e)
    { if (_model.Conversation is { } state) ShowOwnedWindow(new QueueWindow(Runtime, state)); }
    private void OpenBusySendMenu(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        foreach (var item in menu.Items.OfType<MenuItem>()) item.IsChecked = item.Tag as string == _model.BusySendMode;
        menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true;
    });
    private void SelectBusySendMode(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if ((sender as MenuItem)?.Tag is string mode && _model.BusySendModes.Contains(mode)) _model.BusySendMode = mode;
    });
    private async void StartCodeReview(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!_model.CanReview || _model.SelectedProject is not { } project || _model.SelectedModel is not { } model) return;
        var dialog = PrepareDialog(new CodeReviewWindow());
        if (dialog.ShowDialog() != true) return;
        _model.EnsureDraftChat();
        if (_model.SelectedChat is not { } chat) return;
        await Runtime.ReviewAsync(project, chat, model, _model.ApprovalMode, _model.FastMode, dialog.Target);
    });
    private async void StopMenuTask(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (MenuTarget(sender) is { } target) await Runtime.StopAsync(Runtime.Conversation(target.Chat)); });
    private async void ReviewApproval(object sender, RoutedEventArgs e)
    {
        var key = ((sender as FrameworkElement)?.DataContext as TranscriptEntry)?.Message.QuestionBlock?.RequestKey;
        await ReviewRequestAsync(key);
    }
    private (TranscriptEntry Entry, QuestionReply Question)? QuestionTarget(object sender)
    {
        QuestionReply? question = null;
        for (var current = sender as DependencyObject; current is not null; current = GetTranscriptParent(current))
            if (current is FrameworkElement element)
            {
                if (element.DataContext is QuestionReply found) question ??= found;
                if (element.DataContext is TranscriptEntry entry && question is not null && entry.Message.Questions.Contains(question)) return (entry, question);
            }
        return null;
    }
    private async void SelectQuestionOption(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (QuestionTarget(sender) is not { } target || !target.Entry.Message.CanAnswer || (sender as FrameworkElement)?.DataContext is not QuestionChoice option || _model.Conversation is not { } state) return;
        target.Question.SelectAnswer(option);
        await Runtime.AnswerQuestionAsync(state, target.Entry.Message, target.Question, option.Label);
    });
    private async void SendCustomQuestionAnswer(object sender, RoutedEventArgs e) => await SendCustomQuestionAsync(sender);
    private async void CustomQuestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true; await SendCustomQuestionAsync(sender);
    }
    private Task SendCustomQuestionAsync(object sender) => RunAsync(async () =>
    {
        if (QuestionTarget(sender) is not { } target || _model.Conversation is not { } state || string.IsNullOrWhiteSpace(target.Question.CustomAnswer)) return;
        await Runtime.AnswerQuestionAsync(state, target.Entry.Message, target.Question, target.Question.CustomAnswer);
    });
    public Task ReviewRequestAsync(string? key) => RunAsync(async () =>
    {
        var request = key is null ? Runtime.Approvals.FirstOrDefault() : Runtime.Approvals.FirstOrDefault(approval => approval.Key == key);
        if (request is null) return;
        _model.SelectRemoteChat(request.ThreadId);
        var selected = _model.Conversation?.Messages.FirstOrDefault(message => message.QuestionBlock?.RequestKey == request.Key)?.Questions
            .Where(question => question.SelectedAnswer is not null).ToDictionary(question => question.Id, question => question.SelectedAnswer!);
        var dialog = PrepareDialog(new ApprovalWindow(request, _model.ProfileName, selected));
        var accepted = dialog.ShowDialog() == true;
        await Runtime.ResolveApprovalAsync(request, accepted, dialog.ForSession, accepted ? dialog.Answers : null);
    });
    private void CopyMessage(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (sender is Button copy) CopyFeedback.Reset(copy);
        var text = (sender as FrameworkElement)?.DataContext switch { ChatMessage message => message.CopyText, TranscriptEntry entry => entry.Text, _ => "" };
        if (text.Length > 0)
        {
            Clipboard.SetText(text);
            if (sender is Button copied) CopyFeedback.Show(copied);
        }
    });

    private void OpenAccounts(object sender, RoutedEventArgs e) => _windows.ShowAccounts(HostWindow, DialogAnchor);
    private void OpenUsageResets(object sender, RoutedEventArgs e) => Guard(() => _windows.OpenUsageResets(_model.ProfileId, HostWindow, DialogAnchor));
    private void NewProject(object sender, RoutedEventArgs e) => EditProject(null);
    private void EmptyAction(object sender, RoutedEventArgs e)
    { if (_model.HasProject) NewChat(sender, e); else EditProject(null); }
    private void ManageFolders(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SessionTreeNode)
        { if (ProjectTarget(sender) is { } project) EditProject(project); }
        else EditProject(_model.SelectedProject);
    }
    private ProjectState? ProjectTarget(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionTreeNode { CanManageProject: true, ProjectId: { } id } node || node.ProfileId != _model.ProfileId) return null;
        return _controller.Profile(node.ProfileId).Projects.FirstOrDefault(project => project.Id == id);
    }
    private void ProjectNewChat(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (sender is FrameworkElement { DataContext: SessionTreeNode node }) _model.NewTreeChat(node);
    });
    private void SessionTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SessionTreeNode node) Guard(() => _model.ActivateTreeNode(node));
    }
    private void OpenTreeProject(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if ((sender as FrameworkElement)?.DataContext is SessionTreeNode node) _model.ActivateTreeNode(node);
    });
    private void EditProject(ProjectState? project) => Guard(() =>
    {
        var dialog = PrepareDialog(new ManageFoldersWindow(_controller, _model.ProfileId, project));
        if (dialog.ShowDialog() == true) _model.SelectProject(dialog.SavedProjectId);
    });
    private void NewChat(object sender, RoutedEventArgs e) => Guard(() =>
    { if (_model.SelectedProject is { } project) _model.SelectChat(_controller.AddChat(_model.ProfileId, project.Id)); });
    private void FocusSearch(object sender, RoutedEventArgs e) { _model.ExpandSearch(); Dispatcher.BeginInvoke(new Action(() => SearchBox.Focus())); }
    private void SearchFocused(object sender, KeyboardFocusChangedEventArgs e) => _model.ExpandSearch();
    private void SearchUnfocused(object sender, KeyboardFocusChangedEventArgs e) => _model.CollapseSearch();
    private void SearchKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { _model.CollapseSearch(); SearchButton.Focus(); e.Handled = true; } }
    private void ToggleArchived(object sender, RoutedEventArgs e) => _model.ToggleArchived();
    private static void OpenMenu(object sender, PlacementMode placement = PlacementMode.Bottom)
    { if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.Placement = placement; menu.IsOpen = true; } }
    private void OpenProjectMenu(object sender, RoutedEventArgs e) => OpenMenu(sender);
    private void OpenChatMenu(object sender, RoutedEventArgs e) => OpenMenu(sender);
    private void OpenSettings(object sender, RoutedEventArgs e) => OpenMenu(sender);
    private void EditCodexInstructions(object sender, RoutedEventArgs e) => Guard(() => PrepareDialog(new CodexInstructionsWindow(_controller, _model.ProfileId)).ShowDialog());
    private void ClearTemporaryData(object sender, RoutedEventArgs e) => Guard(() => PrepareDialog(new TemporaryDataWindow(_controller, Runtime)).ShowDialog());
    private (ProjectState Project, ChatState Chat)? MenuTarget(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionListItem row || !row.CanManage || row.ProfileId != _model.ProfileId) return null;
        var project = _controller.Profile(row.ProfileId).Projects.FirstOrDefault(item => item.Id == row.ProjectId);
        var chat = project?.Chats.FirstOrDefault(item => item.Id == row.ChatId);
        return project is not null && chat is not null ? (project, chat) : null;
    }
    private async void OpenSession(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if ((sender as FrameworkElement)?.DataContext is SessionListItem row) await _windows.OpenSessionAsync(row); });
    private async void OpenSessionWindow(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if ((sender as FrameworkElement)?.DataContext is SessionListItem row) await _windows.OpenSessionAsync(row, true); });
    private async void RenameChat(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (MenuTarget(sender) is not { } target) return;
        var (project, chat) = target;
        var dialog = PrepareDialog(new TextPromptWindow("Rename chat", "Chat name", chat.Name));
        if (dialog.ShowDialog() == true) await RenameChatAsync(project, chat, dialog.Value);
    });
    private void RenameSelectedChat(object sender, RoutedEventArgs e)
    {
        if (_closing || _model.SelectedProject is not { } project || _model.SelectedChat is not { } chat) return;
        _headerRenameTarget = (project, chat);
        ChatNameInput.Text = chat.Name;
        ChatNameButton.Visibility = Visibility.Collapsed;
        ChatNameInput.Visibility = Visibility.Visible;
        ChatNameInput.Focus();
        ChatNameInput.SelectAll();
    }
    private async void ChatNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        e.Handled = true;
        var save = FinishHeaderRenameAsync(e.Key == Key.Enter);
        ChatNameButton.Focus();
        await save;
    }
    private async void ChatNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => await FinishHeaderRenameAsync(true);
    private async void ChatNameOutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (_headerRenameTarget is not null && !ChatNameInput.IsMouseOver) await FinishHeaderRenameAsync(true);
    }
    private async void ChatNameContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_headerRenameTarget is { } target && (e.NewValue as ChatState)?.Id != target.Chat.Id)
            await FinishHeaderRenameAsync(true);
    }
    private Task FinishHeaderRenameAsync(bool save)
    {
        if (_headerRenameTarget is not { } target) return Task.CompletedTask;
        _headerRenameTarget = null;
        var name = ChatNameInput.Text.Trim();
        ChatNameInput.Visibility = Visibility.Collapsed;
        ChatNameButton.Visibility = Visibility.Visible;
        if (!save || name.Length == 0 || name == target.Chat.Name) return Task.CompletedTask;
        return RunAsync(() => RenameChatAsync(target.Project, target.Chat, name));
    }
    private async Task RenameChatAsync(ProjectState project, ChatState chat, string name)
    {
        await Runtime.EditChatAsync(project, chat, "rename", name);
        _windows.Sessions.Invalidate(_model.ProfileId, chat.CodexThreadId);
    }
    private async void DuplicateChat(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (MenuTarget(sender) is { } target)
            _model.SelectProjectChat(target.Project.Id, await Runtime.DuplicateChatAsync(target.Project, target.Chat));
    });
    private void PinChat(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (MenuTarget(sender) is { } target)
            _controller.EditChat(_model.ProfileId, target.Project.Id, target.Chat.Id, item => item.IsPinned = !item.IsPinned);
    });
    private async void ArchiveChat(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (MenuTarget(sender) is { } target) { await Runtime.EditChatAsync(target.Project, target.Chat, "archive"); _windows.Sessions.Invalidate(_model.ProfileId, target.Chat.CodexThreadId); }
    });
    private async void DeleteChat(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (MenuTarget(sender) is not { } target) return;
        var (project, chat) = target;
        if (ShowMessage($"Delete '{chat.Name}', its saved draft, queued messages, Codex history, and any spawned child conversations? Project files are kept.", "Delete chat", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
        { await Runtime.EditChatAsync(project, chat, "delete"); _windows.Sessions.Invalidate(_model.ProfileId, chat.CodexThreadId); }
    });
    private void About(object sender, RoutedEventArgs e) => ShowMessage("Mini Codex\nUnofficial native Windows client.\n\nCodex CLI 0.162.1 App Server\nSeparate account homes with Codex-managed keyring credentials.\nUse each conversation in one client at a time.", "About Mini Codex", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Exit(object sender, RoutedEventArgs e) => _windows.Exit(DialogAnchor);
}
