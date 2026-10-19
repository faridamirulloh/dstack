using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using MiniCodex.Codex;
using MiniCodex.Models;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.ViewModels;
using MiniCodex.Views;

internal static partial class Program
{
    private static int _checks;
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--question-server", var log]) { RunQuestionServer(log); return; }
        try { if (args is ["--visualization"]) RunVisualizationChecks(); else RunChecks(); }
        catch (Exception error) { Console.Error.WriteLine(error); Application.Current?.Shutdown(1); Environment.Exit(1); }
    }
    private static void RunChecks()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MiniCodex;component/Presentation/Theme.xaml", UriKind.Relative) });
        app.Resources.Add("BoolVisibility", new BooleanToVisibilityConverter());
        CheckComposer();
        // All state and drafts stay inside this ignored build directory; no live Codex server is used.
        var folder = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var store = new StateStore(Path.Combine(folder, "ui-state.json"));
        var controller = new AppController(store);
        var profileId = controller.State.Profiles[0].Id;
        var old = DateTimeOffset.UtcNow.AddDays(-3);
        ProjectState Project(string name, int day, bool match = true) => new()
        {
            Name = name, Folders = [folder], PrimaryFolder = folder,
            Chats = [new ChatState { Name = match ? "match " + name : name, CreatedAt = old.AddDays(day), CodexThreadId = "saved-" + name, IsPinned = name == "Alpha" }]
        };
        var alpha = Project("Alpha", 0); var beta = Project("Beta", 1, false); var gamma = Project("Gamma", 0);
        gamma.Chats.Add(new ChatState { Name = "Archived recent", CodexThreadId = "saved-archived", IsArchived = true, CreatedAt = old.AddDays(2) });
        controller.Commit(state => AppController.Profile(state, profileId).Projects = [alpha, beta, gamma]);
        var drafts = new DraftStore(controller, Path.Combine(folder, "drafts.json"));
        var windows = new WindowManager(controller, drafts);
        using var model = new AccountViewModel(controller, profileId, windows.Runtimes.Get(profileId), windows.Sessions, drafts);
        CheckProjects(model, controller, store, profileId, alpha, beta, gamma);
        CheckThinking(controller, windows, profileId, alpha, beta);
        CheckInstructionsAndTypography(folder);
        CheckQuestionButtons(controller, windows, profileId, alpha, folder);
        CheckUsageResetBrowser(windows.Runtimes.Get(profileId), profileId, folder);
        CheckUsageResetNotifications(controller, windows, profileId);
        drafts.Flush();
        Console.WriteLine($"PASS: {_checks} interaction checks.");
        app.Shutdown();
    }

    private static void Expect(bool success, string description)
    {
        if (!success) throw new InvalidOperationException(description);
        _checks++;
    }
    private static void Equal<T>(T expected, T actual, string description) => Expect(EqualityComparer<T>.Default.Equals(expected, actual), $"{description}: expected '{expected}', got '{actual}'.");
    private static void Insert(MentionInput input, string text) { input.Selection.Text = text; input.CaretIndex = input.Text.Length; }
    private static IEnumerable<InlineUIContainer> Embedded(MentionInput input) => input.Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines.OfType<InlineUIContainer>());

    private static void CheckComposer()
    {
        var input = new MentionInput { Text = "-" }; input.CaretIndex = 1;
        var host = new Window { Content = input, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 500, Height = 300 };
        host.Show(); Pump(25);
        Expect(input.HandleListKey(Key.Space, ModifierKeys.None), "Dash-space starts a list");
        Equal("- ", input.Text, "Bullet Markdown survives formatting");
        Equal("\u2022", ((TextBlock)Embedded(input).Single().Child).Text, "Bullet is styled in the document");
        Insert(input, "first");
        input.HandleListKey(Key.Enter, ModifierKeys.Shift);
        Equal("- first\n- ", input.Text, "Shift+Enter continues a bullet");
        Equal(input.Text.Length, input.CaretIndex, "Caret follows the new marker");
        input.HandleListKey(Key.Tab, ModifierKeys.Shift);
        Equal("- first\n    - ", input.Text, "Shift+Tab indents a bullet");
        Insert(input, "nested"); input.HandleListKey(Key.Enter, ModifierKeys.Shift);
        Equal("- first\n    - nested\n    - ", input.Text, "Continuation retains nesting");
        input.HandleListKey(Key.Back, ModifierKeys.None);
        Equal("- first\n    - nested\n- ", input.Text, "Backspace outdents an empty nested item");
        input.HandleListKey(Key.Enter, ModifierKeys.Shift);
        Equal("- first\n    - nested\n", input.Text, "Empty item exits its list");
        input.Text = "3."; input.CaretIndex = input.Text.Length;
        input.HandleListKey(Key.Space, ModifierKeys.None); Insert(input, "third");
        input.HandleListKey(Key.Enter, ModifierKeys.Shift);
        Equal("3. third\n4. ", input.Text, "Numbered continuation increments the starting number");
        Equal("4.", ((TextBlock)Embedded(input).Last().Child).Text, "Numbering is styled");
        input.Text = "1"; input.CaretIndex = 1; input.HandleListKey(Key.Space, ModifierKeys.None);
        Equal("1. ", input.Text, "A bare number followed by Space starts numbering");
        input.Text = "one\ntwo"; input.Select(0, input.Text.Length); input.ToggleList(false);
        Equal("- one\n- two", input.Text, "Toolbar formats selected lines");
        input.HandleListKey(Key.Tab, ModifierKeys.Shift);
        Equal("    - one\n    - two", input.Text, "Multi-line nesting preserves the selection");
        input.ToggleList(false); Equal("    one\n    two", input.Text, "Toolbar removes selected list markers");
        input.Text = "- hello "; input.CaretIndex = input.Text.Length;
        var reference = new ComposerReference(ReferenceKind.File, "sample.cs", Path.GetFullPath("sample.cs"));
        input.InsertReference(reference, input.CaretIndex, 0); input.CaretIndex = input.Text.Length;
        input.HandleListKey(Key.Enter, ModifierKeys.Shift);
        Expect(input.Text.Contains(reference.Token), "List continuation preserves mention wire text");
        Equal(3, Embedded(input).Count(), "Mention chip and both list markers survive continuation");
        var recovered = new MentionInput { Text = input.Text, References = [reference] };
        Equal(input.Text, recovered.Text, "Recovered list draft preserves all Markdown and mentions");
        Equal(3, Embedded(recovered).Count(), "Recovered list and mention are styled");
        input.Text = "- item"; input.CaretIndex = input.Text.Length;
        input.HandleListKey(Key.Enter, ModifierKeys.Shift);
        Expect(input.CanUndo, "List continuation records an undo transaction"); input.Undo();
        Equal("- item", input.Text, "List continuation can be undone"); input.Redo();
        Equal("- item\n- ", input.Text, "List continuation can be redone");
        input.Text = "plain"; input.CaretIndex = 5; input.HandleListKey(Key.Enter, ModifierKeys.Shift);
        Equal("plain\n", input.Text, "Plain Shift+Enter stays a newline");
        Expect(!input.HandleListKey(Key.Tab, ModifierKeys.Shift), "Non-list Shift+Tab remains navigation");
        input.Text = "well-known"; input.CaretIndex = input.Text.Length;
        Expect(!input.HandleListKey(Key.Space, ModifierKeys.None), "Mid-sentence dash is not a list");
        var legacy = JsonSerializer.Deserialize<ProfileState>("{\"Name\":\"Legacy\",\"Projects\":[]}")!;
        Expect(legacy.OrderProjectsByLastActive && legacy.CustomProjectOrder.Count == 0, "Old settings default to recency ordering");
        host.Close();
    }

    private static void CheckProjects(AccountViewModel model, AppController controller, StateStore store, Guid profile, ProjectState alpha, ProjectState beta, ProjectState gamma)
    {
        string Names() => string.Join(",", model.SessionProjects.Select(node => node.Name));
        Equal("Gamma,Beta,Alpha", Names(), "Default ordering uses activity including archived chats, regardless of pins");
        model.SelectProject(beta.Id);
        Equal("Gamma,Beta,Alpha", Names(), "Opening the second project keeps its position");
        model.SelectProjectChat(alpha.Id, alpha.Chats[0].Id);
        Equal("Gamma,Beta,Alpha", Names(), "Opening a chat in the third project keeps its position");
        model.SelectedProject = controller.Profile(profile).Projects.Single(project => project.Id == beta.Id);
        model.SelectedChat = controller.Profile(profile).Projects.Single(project => project.Id == beta.Id).Chats[0];
        Equal("Gamma,Beta,Alpha", Names(), "Project and chat property selection keeps the order");
        Expect(model.SessionProjects.Single(node => node.ProjectId == beta.Id).Children.Single(node => node.Session?.ChatId == beta.Chats[0].Id).IsSelected, "Chat selection still updates the tree highlight");
        model.SelectProjectChat(alpha.Id, alpha.Chats[0].Id);
        model.SelectChat(alpha.Chats[0].Id);
        Equal("Gamma,Beta,Alpha", Names(), "SelectChat keeps the existing project order");
        var emptyChat = controller.AddChat(profile, alpha.Id);
        model.SelectChat(emptyChat);
        Equal("Gamma,Beta,Alpha", Names(), "Creating and selecting an unsent draft does not reorder projects");
        Expect(store.Load().Profiles.Single(item => item.Id == profile).Projects.All(project => project.LastActiveAt == default), "Navigation does not persist task activity timestamps");
        model.Search = "match";
        Equal("Gamma,Alpha", Names(), "Search retains activity order");
        var first = model.SessionProjects.Single(node => node.ProjectId == alpha.Id);
        var third = model.SessionProjects.Single(node => node.ProjectId == gamma.Id);
        model.MoveProject(first, third, false);
        Expect(!model.OrderProjectsByLastActive, "Dropping a project deactivates automatic sorting");
        model.Search = "";
        Equal("Alpha,Gamma,Beta", Names(), "Custom order retains the project hidden by search");
        model.SelectProject(beta.Id);
        controller.MarkProjectTaskStarted(profile, beta.Id);
        Equal("Alpha,Gamma,Beta", Names(), "A task start does not change custom ordering");
        var saved = store.Load().Profiles.Single(item => item.Id == profile);
        Expect(!saved.OrderProjectsByLastActive && saved.CustomProjectOrder.Count == 3, "Custom order persists to disk");
        controller.Commit(state => AppController.Project(state, profile, alpha.Id).CodexProjectId = "linked-alpha");
        Equal("Alpha,Gamma,Beta", Names(), "Linking a remote project retains its custom position");
        model.UseLastActiveProjectOrder();
        Equal("Beta,Gamma,Alpha", Names(), "Activating the icon restores recency order");
        Expect(store.Load().Profiles.Single(item => item.Id == profile).OrderProjectsByLastActive, "Automatic sort preference persists");
        controller.MarkProjectTaskStarted(profile, alpha.Id);
        Equal("Alpha,Beta,Gamma", Names(), "A newly started task moves its project first");
        controller.MarkProjectTaskStarted(profile, beta.Id);
        Equal("Beta,Alpha,Gamma", Names(), "The next task start takes precedence over the previous one");
        var activity = controller.Profile(profile).Projects.ToDictionary(project => project.Id, project => project.LastActiveAt);
        model.SelectProjectChat(alpha.Id, alpha.Chats[0].Id);
        model.SelectProject(gamma.Id);
        Equal("Beta,Alpha,Gamma", Names(), "Browsing other projects preserves the latest task-start order");
        Expect(store.Load().Profiles.Single(item => item.Id == profile).Projects.All(project => project.LastActiveAt == activity[project.Id]), "Browsing preserves persisted task-start timestamps");
        controller.EditChat(profile, alpha.Id, alpha.Chats[0].Id, chat => chat.CreatedAt = DateTimeOffset.UtcNow.AddHours(1));
        Equal("Beta,Alpha,Gamma", Names(), "Later saved-history timestamps cannot override recorded task-start order");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static void CheckThinking(AppController controller, WindowManager windows, Guid profile, ProjectState alpha, ProjectState beta)
    {
        // Suppress AccountPane's connection-on-load handler in this test process.
        EventManager.RegisterClassHandler(typeof(AccountPane), FrameworkElement.LoadedEvent, new RoutedEventHandler((_, e) => e.Handled = true));
        var window = new AccountWindow(controller, windows, profile) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        var pane = window.Panes.Single();
        var model = (AccountViewModel)pane.DataContext;
        model.SelectProjectChat(alpha.Id, alpha.Chats[0].Id);
        var state = model.Conversation!;
        state.TaskStartedAt = DateTimeOffset.UtcNow.AddSeconds(-125); state.Submitting = true;
        state.Put("user", "You", "test"); state.Put("reply", "Codex", "", streaming: true);
        model.Runtime.Report(new Exception("Test state update"));
        window.Show(); Pump(150); Invoke(pane, "UpdateThinking");
        var indicator = (StackPanel)pane.FindName("ThinkingIndicator"); var label = (TextBlock)pane.FindName("ThinkingText");
        Equal(Visibility.Visible, indicator.Visibility, "Running task with no revealed text shows thinking");
        Expect(label.Text.StartsWith("Thinking \u00b7 2:"), "Elapsed time starts from submission time");
        var startedAt = state.TaskStartedAt;
        state.Put("reply", "Codex", string.Concat(Enumerable.Repeat("Streaming text. ", 100)), delta: true, streaming: true);
        model.Runtime.Report(new Exception("Test state update")); Pump(35); Invoke(pane, "UpdateThinking");
        if (SystemParameters.ClientAreaAnimation) Equal(Visibility.Collapsed, indicator.Visibility, "Text reveal hides thinking");
        Pump(2000); Invoke(pane, "UpdateThinking");
        Equal(Visibility.Visible, indicator.Visibility, "Stream pause restores thinking even while item is streaming");
        Equal(startedAt, state.TaskStartedAt, "Streaming does not reset elapsed time");
        model.SelectProjectChat(beta.Id, beta.Chats[0].Id); Invoke(pane, "UpdateThinking");
        Equal(Visibility.Collapsed, indicator.Visibility, "Switching to an idle chat hides another chat's thinking");
        model.SelectProjectChat(alpha.Id, alpha.Chats[0].Id); Invoke(pane, "UpdateThinking");
        Equal(Visibility.Visible, indicator.Visibility, "Returning to a running chat restores thinking");
        state.Uncertain = true; Invoke(pane, "UpdateThinking");
        Equal(Visibility.Collapsed, indicator.Visibility, "Unknown outcome does not claim to be thinking");
        state.Uncertain = false; state.Submitting = false; state.Terminal.TrySetResult(); Invoke(pane, "UpdateThinking");
        Equal(Visibility.Collapsed, indicator.Visibility, "Task completion stops thinking");
        typeof(AccountWindow).GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close();
    }
}
