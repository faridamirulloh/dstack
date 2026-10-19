using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MiniCodex.Presentation;

internal static class FileLinks
{
    private static readonly Regex LocationSuffix = new(@"(?:#L\d+(?:C\d+)?(?:-L?\d+(?:C\d+)?)?|:\d+(?::\d+)?)$", RegexOptions.NonBacktracking);
    public static bool TryResolve(string target, string workingDirectory, out string path, bool hasLocation = false)
    {
        path = "";
        try
        {
            var candidate = target.Trim().Trim('<', '>');
            if (hasLocation) candidate = LocationSuffix.Replace(candidate, "");
            if (hasLocation && candidate.Contains('%')) candidate = Uri.UnescapeDataString(candidate);
            if (candidate.Length == 0 || candidate.StartsWith('#')) return false;
            if (candidate.Length > 3 && candidate[0] == '/' && char.IsAsciiLetter(candidate[1]) && candidate[2] == ':') candidate = candidate[1..];
            if (Path.IsPathFullyQualified(candidate)) path = Path.GetFullPath(candidate);
            else if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile) return false;
                path = Path.GetFullPath(uri.LocalPath);
            }
            else
            {
                if (!Path.IsPathFullyQualified(workingDirectory)) return false;
                path = Path.GetFullPath(candidate, workingDirectory);
            }
            return Path.IsPathFullyQualified(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UriFormatException) { return false; }
    }

    public static string Name(string path)
    {
        var value = path.TrimEnd('\\', '/');
        var separator = Math.Max(value.LastIndexOf('\\'), value.LastIndexOf('/'));
        var name = value[(separator + 1)..];
        return string.IsNullOrEmpty(name) ? "Folder" : name;
    }
    public static Hyperlink Link(string path, bool requireControl = false, string? label = null)
    {
        var link = new Hyperlink(new Run(label ?? Name(path)))
        {
            Foreground = (Brush)Application.Current.FindResource("FileLinkBrush"), Cursor = Cursors.Hand,
            ToolTip = path + (requireControl ? "\nCtrl+click to open. Right-click for file actions." : "\nClick to open. Right-click for file actions.") + "\nDrag to copy the original file or folder."
        };
        EnableDrag(link, () => path);
        link.Click += (_, e) => { e.Handled = true; if (!requireControl || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Open(path); };
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open file" }; open.Click += (_, _) => Open(path);
        var explore = new MenuItem { Header = "Show in Explorer" }; explore.Click += (_, _) => ShowInExplorer(path);
        menu.Items.Add(open); menu.Items.Add(explore); link.ContextMenu = menu;
        return link;
    }
    public static void EnableDrag(IInputElement source, Func<string?> getPath)
    {
        Point? start = null;
        var dragged = false;
        source.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragged = false;
            start = getPath() is null ? null : e.GetPosition(source);
        };
        source.PreviewMouseLeftButtonUp += (_, e) =>
        {
            start = null;
            if (dragged) { e.Handled = true; dragged = false; }
        };
        source.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) { start = null; return; }
            if (start is not { } origin) return;
            var movement = e.GetPosition(source) - origin;
            if (Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            if (getPath() is not { } path) return;
            dragged = true;
            e.Handled = true;
            source.ReleaseMouseCapture();
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                AppDialog.Show("The original file or folder is no longer available.", "Could not drag file");
                return;
            }
            try { DragDrop.DoDragDrop((DependencyObject)source, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy); }
            catch (System.Runtime.InteropServices.ExternalException error)
            { AppDialog.Show(error.Message, "Could not drag file"); }
        };
    }
    public static void Open(string path) => Run(() =>
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException("This file no longer exists. Use Show in Explorer to open its folder.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    });
    public static void ShowInExplorer(string path) => Run(() =>
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
        if (File.Exists(path) || Directory.Exists(path))
        { start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path); }
        else
        {
            var folder = Path.GetDirectoryName(path);
            while (folder is not null && !Directory.Exists(folder)) folder = Path.GetDirectoryName(folder);
            if (folder is null) throw new IOException("The containing folder is unavailable.");
            start.ArgumentList.Add(folder);
        }
        Process.Start(start);
    });
    private static void Run(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or ArgumentException)
        { AppDialog.Show(error.Message, "Could not open file", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
