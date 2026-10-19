using System.ComponentModel;
using System.Diagnostics;

namespace MiniCodex.Services;

public static class ProjectTerminal
{
    public static void Open(string folder)
    {
        var workingDirectory = FolderPaths.Canonicalize(folder);
        var terminal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(terminal))
        {
            var start = StartInfo(terminal, workingDirectory);
            start.ArgumentList.Add("-d"); start.ArgumentList.Add(workingDirectory);
            try { using var process = Process.Start(start); return; }
            catch (Win32Exception) { }
        }
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new IOException("Windows Terminal and Windows PowerShell are unavailable.");
        var fallback = StartInfo(powershell, workingDirectory);
        fallback.ArgumentList.Add("-NoLogo"); fallback.ArgumentList.Add("-NoExit");
        using var shell = Process.Start(fallback);
    }
    private static ProcessStartInfo StartInfo(string executable, string folder) => new(executable)
    {
        WorkingDirectory = folder, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal
    };
}
