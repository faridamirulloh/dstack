using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniCodex.Codex;
using MiniCodex.Models;
using MiniCodex.Services;
using MiniCodex.ViewModels;
using MiniCodex.Views;

internal static partial class Program
{
    // A local JSON-RPC peer exercises the production transport without starting Codex or using an account.
    private static void RunQuestionServer(string log)
    {
        while (Console.ReadLine() is { } line)
        {
            File.AppendAllText(log, line + "\n");
            using var document = JsonDocument.Parse(line);
            var message = document.RootElement;
            if (message.Text("method") is not { } method) continue;
            if (method == "turn/steer") Thread.Sleep(120);
            object result = method switch
            {
                "thread/resume" => new { thread = new { id = "question-thread", status = new { type = "idle" } } },
                "turn/start" => new { turn = new { id = "answer-turn" } },
                _ => new { }
            };
            Console.WriteLine(JsonSerializer.Serialize(new { id = message.Get("id"), result }));
        }
    }

    private static IEnumerable<T> QuestionVisuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var descendant in QuestionVisuals<T>(child)) yield return descendant;
        }
    }
    private static JsonElement QuestionJson(object value) => JsonSerializer.SerializeToElement(value);
    private static void WaitQuestion(Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(8);
        while (!done() && DateTime.UtcNow < end) Pump(20);
        Expect(done(), "Question operation completed");
    }
    private static void CheckQuestionButtons(AppController controller, WindowManager windows, Guid profile, ProjectState project, string folder)
    {
        var runtime = windows.Runtimes.Get(profile);
        var state = runtime.Conversation(project.Chats[0]);
        state.Messages.Clear(); state.Transcript.Clear(); state.ThreadId = "question-thread"; state.Loaded = true;
        controller.EditChat(profile, project.Id, state.LocalId, chat => chat.CodexThreadId = state.ThreadId);
        var log = Path.Combine(folder, "question-rpc.jsonl"); File.WriteAllText(log, "");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--question-server"); start.ArgumentList.Add(log);
        var client = new CodexClient(start, _ => Task.CompletedTask, _ => { });
        typeof(AccountRuntime).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(runtime, client);
        typeof(AccountRuntime).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(runtime, true);
        typeof(AccountRuntime).GetProperty(nameof(AccountRuntime.SignedIn))!.SetValue(runtime, true);
        runtime.Models.Add(new CodexModel("fixture-model", "Fixture", ["medium"], "medium", true));
        var window = new AccountWindow(controller, windows, profile) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1100, Height = 1000 };
        var pane = window.Panes.Single();
        var model = (AccountViewModel)pane.DataContext;
        model.SelectProjectChat(project.Id, state.LocalId);
        window.Show(); Pump(100);
        var transcript = (ListBox)pane.FindName("Transcript");
        void Event(string method, object parameters, int? id = null)
        {
            typeof(AccountRuntime).GetMethod("OnMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime,
                [client, new WireMessage(method, QuestionJson(parameters), id is { } key ? QuestionJson(key) : default, 0)]);
            Pump(30);
        }
        void Show(ChatMessage message)
        {
            transcript.ScrollIntoView(state.Transcript.Single(entry => entry.Message == message));
            window.UpdateLayout(); Pump(80);
        }
        ToggleButton Option(string label) => QuestionVisuals<ToggleButton>(transcript).First(button => button.IsVisible && button.DataContext is QuestionChoice choice && choice.Label == label);
        Button CustomSend(QuestionReply question) => QuestionVisuals<Button>(transcript).First(button => button.IsVisible && button.Content as string == "Send answer" && button.DataContext == question);
        TextBox CustomInput(QuestionReply question) => QuestionVisuals<TextBox>(transcript).First(input => input.IsVisible && input.DataContext == question);
        JsonElement[] Requests() => File.ReadAllLines(log).Where(line => line.Length > 0).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        void AsyncQuestion(string id, params object[] questions) => Event("item/completed", new { threadId = state.ThreadId, item = new { type = "agentMessage", id, text = "Which events should trigger an account’s reset notification?\n- Both quota resets and newly earned reset credits (recommended)\n- Only when usage limits reset\n- Only when new reset credits are earned", delivery = "async", questions } });
        var previousTaskActivity = controller.Profile(profile).Projects.Single(item => item.Id == project.Id).LastActiveAt;
        try
        {
            state.Terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); state.TurnId = "running-turn";
            state.Put("initial-user", "You", "Notify on reset");
            AsyncQuestion("async-choice", new { title = "Which events should trigger an account’s reset notification?", options = new[] { "Both quota resets and newly earned reset credits (recommended)", "Only when usage limits reset", "Only when new reset credits are earned" } });
            var message = state.Messages.Single(message => message.Id == "async-choice"); Show(message);
            Equal(3, message.Questions.Single().Options.Count, "Async string options become three choices");
            Expect(message.HasQuestions && message.DisplayText.Length == 0, "Structured async message replaces raw bullet text");
            Equal("[\"request_user_input_async\",\"async-choice\",0]", message.Questions[0].Id, "Async reply retains Codex's stable question identity");
            var option = Option("Only when usage limits reset");
            Expect(option.IsEnabled, "Async question choice is an enabled button");
            Expect(CustomInput(message.Questions[0]).IsEnabled, "Custom answer is available beside the options");
            var card = (ListBoxItem)transcript.ItemContainerGenerator.ContainerFromItem(state.Transcript.Single(entry => entry.Message == message));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(card), null, new Rect(0, 0, card.ActualWidth, card.ActualHeight));
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var preview = File.Create(Path.Combine(AppContext.BaseDirectory, "question-buttons.png"))) encoder.Save(preview);
            model.Draft = "Keep my unsent main draft";
            option.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            option.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitQuestion(() => message.Questions[0].HasAnswer);
            var steer = Requests().Where(request => request.Text("method") == "turn/steer").ToArray();
            Equal(1, steer.Length, "Repeated choice clicks send one reply");
            var sent = QuestionTranscript.FromUserReply(steer[0].Get("params").Get("input").Items().Single().Text("text")!)!;
            Equal("Only when usage limits reset", sent.Questions[0].Answer, "Click sends the exact selected answer automatically");
            Equal("running-turn", steer[0].Get("params").Text("expectedTurnId"), "Async answer steers the originating active turn");
            Equal("Keep my unsent main draft", model.Draft, "Answering preserves the main composer draft");
            Equal(previousTaskActivity, controller.Profile(profile).Projects.Single(item => item.Id == project.Id).LastActiveAt, "Steering an existing task does not refresh project task-start order");
            Expect(!message.CanAnswer, "Answered card cannot be sent again");
            // Replayed item metadata must not reopen an answered card.
            AsyncQuestion("async-choice", new { title = "Which events?", options = new[] { "Yes", "No" } });
            Expect(message.Questions[0].HasAnswer && !message.CanAnswer, "Replayed question keeps its submitted answer");

            AsyncQuestion("custom", new { title = "How should it behave?", options = new[] { "Default", "Silent" } });
            var custom = state.Messages.Single(message => message.Id == "custom"); Show(custom);
            var customQuestion = custom.Questions[0];
            CustomInput(customQuestion).Text = "Only quota resets\nKeep credits silent";
            CustomSend(customQuestion).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitQuestion(() => custom.Questions[0].HasAnswer);
            Equal("Only quota resets\nKeep credits silent", custom.Questions[0].Answer, "Inline custom reply sends multiline text");
            Equal(2, Requests().Count(request => request.Text("method") == "turn/steer"), "Custom answer is sent without opening a dialog");

            AsyncQuestion("free-text", new { title = "Anything else?" });
            var free = state.Messages.Single(message => message.Id == "free-text"); Show(free);
            Equal(0, free.Questions[0].Options.Count, "Free-text-only question is supported");
            CustomSend(free.Questions[0]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(150);
            Equal(2, Requests().Count(request => request.Text("method") == "turn/steer"), "Empty custom answer is not submitted");
            CustomInput(free.Questions[0]).Text = "No more changes";
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            CustomInput(free.Questions[0]).RaiseEvent(enter);
            WaitQuestion(() => free.Questions[0].HasAnswer);
            Expect(enter.Handled, "Enter sends a custom answer in place");

            AsyncQuestion("siblings", new { title = "First setting?", options = new[] { "First setting" } }, new { title = "Second setting?", options = new[] { "Second setting" } });
            var siblings = state.Messages.Single(message => message.Id == "siblings"); Show(siblings);
            siblings.Questions[1].CustomAnswer = "Keep this other reply draft";
            Option("First setting").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitQuestion(() => siblings.Questions[0].HasAnswer);
            Expect(siblings.CanAnswer && !siblings.Questions[1].HasAnswer, "Async questions submit independently without answering siblings");
            Equal("Keep this other reply draft", siblings.Questions[1].CustomAnswer, "Sending one async answer retains another question's draft");
            Show(siblings); CustomSend(siblings.Questions[1]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitQuestion(() => siblings.Questions.All(question => question.HasAnswer));

            // Native blocking questions use the same one-click behavior and standard answers RPC.
            Event("item/tool/requestUserInput", new { threadId = state.ThreadId, turnId = state.TurnId, itemId = "native", questions = new[] { new { id = "scope", header = "Scope", question = "Which limits?", options = new[] { new { label = "Quota only", description = "Ignore reset credits" }, new { label = "Both", description = "Include credits" } } } } }, 701);
            var native = state.Messages.Single(message => message.Id == "questions/native"); Show(native);
            Option("Quota only").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitQuestion(() => native.Questions[0].HasAnswer);
            WaitQuestion(() => Requests().Any(request => request.Get("id").Number() == 701));
            Equal("Quota only", Requests().Single(request => request.Get("id").Number() == 701).Get("result").Get("answers").Get("scope").Get("answers").Items().Single().Text(), "Native option click automatically answers its request");

            Event("item/tool/requestUserInput", new { threadId = state.ThreadId, itemId = "native-multiple", questions = new object[] { new { id = "one", header = "One", question = "First?", options = new[] { new { label = "First choice", description = "" } } }, new { id = "two", header = "Two", question = "Second?", options = Array.Empty<object>() } } }, 702);
            var multiple = state.Messages.Single(message => message.Id == "questions/native-multiple"); Show(multiple);
            Option("First choice").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(100);
            Expect(!Requests().Any(request => request.Get("id").Number() == 702), "Multiple blocking questions wait for each explicit answer");
            CustomInput(multiple.Questions[1]).Text = "Second custom";
            CustomSend(multiple.Questions[1]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitQuestion(() => multiple.Questions.All(question => question.HasAnswer));
            WaitQuestion(() => Requests().Any(request => request.Get("id").Number() == 702));
            Equal("Second custom", Requests().Single(request => request.Get("id").Number() == 702).Get("result").Get("answers").Get("two").Get("answers").Items().Single().Text(), "Last custom answer submits the native batch");

            state.Terminal.TrySetResult(); state.TurnId = null;
            AsyncQuestion("idle", new { title = "Continue?", options = new[] { "Continue now" } });
            var idle = state.Messages.Single(message => message.Id == "idle"); Show(idle);
            Option("Continue now").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitQuestion(() => idle.Questions[0].HasAnswer);
            Equal(1, Requests().Count(request => request.Text("method") == "turn/start"), "Idle async answer starts a continuation automatically");
            Expect(controller.Profile(profile).Projects.Single(item => item.Id == project.Id).LastActiveAt > previousTaskActivity, "Starting a continuation through the production send path updates project ordering");
            var turnStart = Requests().Single(request => request.Text("method") == "turn/start");
            Equal("Continue now", QuestionTranscript.FromUserReply(turnStart.Get("params").Get("input").Items().Single().Text("text")!)!.Questions[0].Answer, "Continuation carries the clicked reply");
            state.Terminal.TrySetResult(); state.TurnId = null;

            // Reverse history loading: the reply can arrive before its source question.
            var history = new ConversationState(Guid.NewGuid());
            var historyBlock = QuestionTranscript.FromAsync(QuestionJson(new[] { new { title = "Saved question?", options = new[] { "Saved answer" } } }), "saved");
            typeof(AccountRuntime).GetMethod("RenderItem", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [history, QuestionJson(new { type = "userMessage", id = "saved-reply", content = new[] { new { type = "text", text = QuestionTranscript.ReplyMessage(historyBlock.Questions[0], "Saved answer") } } }), true, false]);
            typeof(AccountRuntime).GetMethod("RenderItem", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [history, QuestionJson(new { type = "agentMessage", id = "saved", text = "Saved question?\n- Saved answer", questions = new[] { new { title = "Saved question?", options = new[] { "Saved answer" } } } }), true, false]);
            Equal("Saved answer", history.Messages[0].Questions[0].Answer, "History restores answered async cards even in reverse order");
            Expect(!history.Messages[0].CanAnswer, "History does not resurrect resolved question actions");
            Equal("Saved answer", QuestionTranscript.Parse(history.Messages[0].QuestionBlock!.Serialize())!.Questions[0].Answer, "Question transcript serialization preserves answers");
            history.Put("old-unanswered", "Codex", "Old question?");
            history.Messages.Last().SetQuestions(historyBlock);
            history.Put("new-user", "You", "Move on"); history.ReconcileQuestionReplies();
            Expect(!history.Messages.Single(message => message.Id == "old-unanswered").CanAnswer, "A later ordinary user message closes an old unanswered card");
            history.Put("ordinary", "Codex", "Things changed:\n- First item\n- Second item");
            Expect(!history.Messages.Last().HasQuestions, "Ordinary Markdown bullet lists stay ordinary text");
            AsyncQuestion("disconnect", new { title = "Pending question?", options = new[] { "Pending choice" } });
            var disconnected = state.Messages.Single(message => message.Id == "disconnect"); Show(disconnected);
            Expect(Option("Pending choice").IsEnabled, "Pending question is actionable before disconnect");
            typeof(AccountRuntime).GetMethod("OnDisconnected", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(runtime, ["Fixture disconnected"]);
            Pump(40);
            Expect(!Option("Pending choice").IsEnabled && !CustomInput(disconnected.Questions[0]).IsEnabled, "Disconnect disables pending options and custom input");
        }
        finally
        {
            state.Submitting = false; state.Uncertain = false; state.TurnId = null; state.Terminal.TrySetResult();
            typeof(AccountWindow).GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); window.Close();
            var dispose = client.DisposeAsync().AsTask(); WaitQuestion(() => dispose.IsCompleted); dispose.GetAwaiter().GetResult();
            typeof(AccountRuntime).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(runtime, null);
            typeof(AccountRuntime).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(runtime, false);
            runtime.Models.Clear();
        }
    }
}
