using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniCodex.Presentation;
using MiniCodex.Services;
using MiniCodex.Views;

internal static partial class Program
{
    private static void InstructionsFail(Action action, string description)
    {
        try { action(); }
        catch (IOException) { Expect(true, description); return; }
        throw new InvalidOperationException(description);
    }
    private static void CheckInstructionsAndTypography(string folder)
    {
        var root = Path.Combine(folder, "instructions");
        var firstHome = Path.Combine(root, "account-one"); var secondHome = Path.Combine(root, "account-two");
        var first = new CodexInstructionsStore(firstHome); var second = new CodexInstructionsStore(secondHome);
        var empty = first.Load();
        Expect(empty.Text.Length == 0 && !empty.IsOverride && !Directory.Exists(firstHome), "Opening instructions for a new account does not create files");
        first.Save(empty, "");
        Expect(!Directory.Exists(firstHome), "Saving unchanged empty instructions does not create a file");
        const string preferences = "# Working preferences\n\n- Keep answers concise.\n- Run relevant checks before finishing.\n- Preserve Unicode: 日本語 ✓";
        var saved = first.Save(empty, preferences);
        Equal(preferences, File.ReadAllText(Path.Combine(firstHome, "AGENTS.md")), "Instructions save to the account's native AGENTS.md");
        Equal(preferences, first.Load().Text, "Saved multiline Unicode instructions reload unchanged");
        Expect(second.Load().Text.Length == 0 && !Directory.Exists(secondHome), "Instructions stay isolated from another account");
        var stale = first.Load(); saved = first.Save(saved, preferences + "\n- Prefer readable code.");
        InstructionsFail(() => first.Save(stale, "stale window"), "A stale editor cannot overwrite a newer save");
        Equal(saved.Text, first.Load().Text, "A conflicting save preserves the current instructions");
        var overridePath = Path.Combine(firstHome, "AGENTS.override.md");
        File.WriteAllText(overridePath, "Temporary preferences");
        var overriding = first.Load();
        Expect(overriding.IsOverride && overriding.FilePath == overridePath && overriding.Text == "Temporary preferences", "The editor selects the active native override");
        InstructionsFail(() => first.Save(saved, "overwrite new override"), "An override appearing after opening is detected as a conflict");
        overriding = first.Save(overriding, "Updated temporary preferences");
        Equal(saved.Text, File.ReadAllText(saved.FilePath), "Editing an override preserves the base instructions");
        var restored = first.Save(overriding, "");
        Expect(!restored.IsOverride && restored.Text == saved.Text, "Clearing an override restores native base instructions");
        var bomText = "# Preferences\nKeep the existing file's text.";
        File.WriteAllBytes(saved.FilePath, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(bomText)).ToArray());
        Equal(bomText, first.Load().Text, "Existing UTF-8 BOM instructions open cleanly");
        var valid = first.Load();
        InstructionsFail(() => first.Save(valid, new string('x', CodexInstructionsStore.MaxBytes + 1)), "Oversized edits do not replace instructions");
        Equal(bomText, first.Load().Text, "Rejected edits preserve the file");
        File.WriteAllBytes(saved.FilePath, [0xff, 0xfe, 0xff]);
        InstructionsFail(() => first.Load(), "Invalid UTF-8 reports a read failure");
        Expect(File.ReadAllBytes(saved.FilePath).SequenceEqual(new byte[] { 0xff, 0xfe, 0xff }), "A read failure does not rewrite the original file");
        File.WriteAllText(saved.FilePath, preferences, new UTF8Encoding(false));

        var controller = new AppController(new StateStore(Path.Combine(root, "state.json")));
        var profile = controller.State.Profiles[0].Id;
        controller.Commit(state => AppController.Profile(state, profile).ExistingCodexHome = firstHome);
        bool? Dialog(Action<CodexInstructionsWindow> action)
        {
            var dialog = new CodexInstructionsWindow(controller, profile)
            { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            deadline.Tick += (_, _) => { deadline.Stop(); throw new InvalidOperationException("Instructions dialog did not finish: " + ((TextBlock)dialog.FindName("Status")).Text); };
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => action(dialog)));
            deadline.Start();
            try { return dialog.ShowDialog(); }
            finally { deadline.Stop(); }
        }
        var beforeCancel = File.ReadAllBytes(saved.FilePath);
        Dialog(dialog =>
        {
            Equal(preferences, ((TextBox)dialog.FindName("Input")).Text, "The instruction editor opens existing account guidance");
            Expect(((TextBlock)dialog.FindName("Scope")).Text.Contains("All projects in this account"), "The editor makes its account scope visible");
            dialog.Close();
        });
        Expect(File.ReadAllBytes(saved.FilePath).SequenceEqual(beforeCancel), "Closing an unchanged editor leaves the file intact");
        const string fromEditor = "# My instructions\r\n\r\nKeep replies concise.\r\nRun relevant checks before finishing.";
        Expect(Dialog(dialog =>
        {
            ((TextBox)dialog.FindName("Input")).Text = fromEditor;
            var content = (FrameworkElement)dialog.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var image = File.Create(Path.Combine(AppContext.BaseDirectory, "codex-instructions.png"))) encoder.Save(image);
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }) == true, "Save instructions closes the editor successfully");
        Equal(fromEditor, first.Load().Text, "The editor saves its custom instructions to the native account file");
        Equal("", second.Load().Text, "Saving in the editor leaves another account unchanged");
        Directory.CreateDirectory(secondHome);
        Dialog(dialog =>
        {
            ((TextBox)dialog.FindName("Input")).Text = "Do not write this to a changed account home.";
            controller.Commit(state => AppController.Profile(state, profile).ExistingCodexHome = secondHome);
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Expect(((TextBlock)dialog.FindName("Status")).Text.Contains("home changed"), "An account-home change is reported before saving");
            typeof(CodexInstructionsWindow).GetField("_saved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dialog, true); dialog.Close();
        });
        Equal(fromEditor, first.Load().Text, "A changed account home preserves the original instructions");
        Expect(!File.Exists(Path.Combine(secondHome, "AGENTS.md")), "A changed account home does not receive unintended instructions");

        var composer = new MentionInput { Text = "Windows system font in the composer" };
        var reply = new MarkdownMessageView { Text = "Windows system font in **chat prose**.", Role = "Codex" };
        var host = new Window { Style = (Style)Application.Current.FindResource(typeof(Window)), Content = new StackPanel { Children = { composer, reply } },
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 500, Height = 250 };
        host.Show(); Pump(200);
        Equal(SystemFonts.MessageFontFamily.Source, host.FontFamily.Source, "Window controls use the Windows message font");
        Equal(SystemFonts.MessageFontFamily.Source, composer.Document.FontFamily.Source, "The rich composer uses the Windows message font");
        Equal(SystemFonts.MessageFontFamily.Source, reply.Document.FontFamily.Source, "Chat prose uses the Windows message font");
        try
        {
            Application.Current.Resources[SystemFonts.MessageFontFamilyKey] = new FontFamily("Arial"); Pump(50);
            Equal("Arial", host.FontFamily.Source, "Controls follow changes to the system font resource");
            Equal("Arial", composer.Document.FontFamily.Source, "Composer follows changes to the system font resource");
            Equal("Arial", reply.Document.FontFamily.Source, "Chat prose follows changes to the system font resource");
        }
        finally { Application.Current.Resources.Remove(SystemFonts.MessageFontFamilyKey); host.Close(); }
    }
}
