using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MiniCodex.Services;
using MiniCodex.Models;

namespace MiniCodex.Codex;

public static class CodexInstallation
{
    public const string Version = "0.162.1";
    public static string Home(Guid id) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCodex", "profiles", id.ToString("N"), "codex-home");
    public static string Home(ProfileState profile) => profile.ExistingCodexHome ?? Home(profile.Id);

    public static string Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured)) throw new IOException("Choose an existing absolute path to the official Codex executable.");
            return Path.GetFullPath(configured);
        }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "codex", Version, "bin", "codex.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new IOException("Codex CLI 0.162.1 is missing. Use Settings → Choose Codex executable and select bin/codex.exe from the complete official Windows package.");
    }

    public static ProcessStartInfo StartInfo(string executable, string home)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = home
        };
        foreach (var key in new[] { "CODEX_HOME", "CODEX_SQLITE_HOME", "OPENAI_API_KEY", "CODEX_API_KEY", "OPENAI_BASE_URL", "OPENAI_API_BASE", "OPENAI_ACCESS_TOKEN", "OPENAI_ORG_ID", "OPENAI_ORGANIZATION", "OPENAI_PROJECT_ID", "AZURE_OPENAI_API_KEY" }) start.Environment.Remove(key);
        start.Environment["CODEX_HOME"] = home;
        var package = Directory.GetParent(Path.GetDirectoryName(executable)!)?.FullName;
        start.Environment["PATH"] = Path.GetDirectoryName(executable) + Path.PathSeparator + Path.Combine(package ?? "", "codex-path") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        return start;
    }

    public static async Task<ProcessStartInfo> PrepareAsync(string? configured, ProfileState profile, IEnumerable<ProfileState> profiles)
    {
        var executable = Resolve(configured);
        if (profile.ExistingCodexHome is null) Directory.CreateDirectory(Home(profile));
        var home = FolderPaths.Canonicalize(Home(profile));
        foreach (var other in profiles.Where(item => item.Id != profile.Id))
            if (Directory.Exists(Home(other)) && FolderPaths.Comparer.Equals(home, FolderPaths.Canonicalize(Home(other))))
                throw new IOException("Another Mini Codex profile already uses this Codex home. Open that profile instead.");
        var check = StartInfo(executable, home); check.ArgumentList.Add("--version");
        using (var process = Process.Start(check) ?? throw new IOException("Cannot check the Codex version."))
        {
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                if (process.ExitCode != 0 || !(await output).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(Version))
                    throw new IOException("This build requires official Codex CLI 0.162.1. Desktop and plugin alpha binaries are not supported.");
                await errors;
            }
            catch { if (!process.HasExited) process.Kill(true); throw; }
        }
        var start = StartInfo(executable, home);
        if (profile.ExistingCodexHome is null)
        {
            start.ArgumentList.Add("-c"); start.ArgumentList.Add("cli_auth_credentials_store=\"keyring\"");
            start.ArgumentList.Add("-c"); start.ArgumentList.Add("sqlite_home=" + JsonSerializer.Serialize(home));
        }
        start.ArgumentList.Add("app-server");
        return start;
    }
}
