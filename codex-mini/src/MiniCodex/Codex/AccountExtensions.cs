using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MiniCodex.Models;
using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    public bool InstallingExtensions { get; private set; }

    private void RequireNoExtensionInstall()
    {
        if (InstallingExtensions) throw new InvalidOperationException("Wait for extension installation to finish before starting work, reconnecting, or closing this account.");
    }

    public async Task<IReadOnlyList<ExtensionPackage>> ListExtensionPluginsAsync(string cwd, CancellationToken cancellation)
    {
        var result = await PluginCommandAsync(cwd, ["plugin", "list", "--available", "--json"], false, cancellation);
        if (result.Get("installed").ValueKind != JsonValueKind.Array || result.Get("available").ValueKind != JsonValueKind.Array)
            throw new IOException("Codex returned an unsupported plugin catalog. Installed status could not be confirmed.");
        var packages = new Dictionary<string, ExtensionPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in new[] { "available", "installed" })
            foreach (var row in result.Get(section).Items())
            {
                var name = row.Text("name"); var marketplace = row.Text("marketplaceName");
                var id = row.Text("pluginId") ?? (name is not null && marketplace is not null ? name + "@" + marketplace : null);
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                var policy = row.Text("installPolicy") ?? "";
                var allowed = !policy.Replace("_", "").Replace("-", "").Equals("NOTAVAILABLE", StringComparison.OrdinalIgnoreCase);
                var auth = row.Text("authPolicy");
                packages[id] = new ExtensionPackage
                {
                    Key = "plugin/" + id, Name = name, Kind = ReferenceKind.Plugin, PluginId = id,
                    Source = marketplace ?? "Codex marketplace", Version = row.Text("version") ?? "",
                    Description = row.Text("description") ?? row.Get("interface").Text("shortDescription") ?? "Plugin from " + (marketplace ?? "a Codex marketplace") + ".",
                    Installed = section == "installed" || row.Get("installed").ValueKind == JsonValueKind.True,
                    Enabled = row.Get("enabled").ValueKind == JsonValueKind.True, Available = allowed,
                    Compatibility = !allowed ? "Installation is unavailable under this marketplace's policy."
                        : "Installed by Codex CLI; permissions and service access are checked by Codex."
                            + (auth is { Length: > 0 } ? " Authentication policy: " + auth + "." : "")
                };
            }
        return packages.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<string> InstallExtensionAsync(ExtensionPackage package, string cwd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (HasWork || LoginPending || _authentication.CurrentCount == 0 || _connect.CurrentCount == 0 || _client?.PendingCount > 0 || _conversations.Values.Any(chat => chat.Loading))
            throw new InvalidOperationException("Wait for this account's tasks, sign-in, history loading, and extension installation to finish first.");
        if (!package.CanInstall) throw new InvalidOperationException("This extension is already installed or unavailable.");
        InstallingExtensions = true; Touch(); Notify();
        try
        {
            if (package.Kind == ReferenceKind.Plugin && package.PluginId is { } id)
            {
                var current = (await ListExtensionPluginsAsync(cwd, CancellationToken.None)).FirstOrDefault(item => string.Equals(item.PluginId, id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new IOException("The plugin is no longer available in the account's catalog. Refresh Browse.");
                if (!current.Available) throw new InvalidOperationException(current.Compatibility);
                if (!current.Installed)
                {
                    var installed = await PluginCommandAsync(cwd, ["plugin", "add", "--json", "--", id], true, CancellationToken.None);
                    if (!string.Equals(installed.Text("pluginId"), id, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Codex did not confirm the expected plugin ID. Refresh installed extensions before retrying.");
                }
            }
            else if (package.Kind == ReferenceKind.Skill && package.Skill is { } source)
            {
                await GitHubSkills.InstallAsync(source, HomePath, CancellationToken.None);
            }
            else throw new InvalidOperationException("This catalog entry cannot be installed.");
            package.Installed = true;
            CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
            return "Installed " + package.Name + ". Discovery is refreshing. New chats pick up the extension; reconnect this account if it remains unavailable. Some plugins require a separate service sign-in.";
        }
        finally
        {
            InstallingExtensions = false; Touch(); Notify();
            foreach (var state in _conversations.Values.Where(chat => chat.Queue.Count > 0 && !chat.QueuePaused).ToArray()) Observe(() => DrainQueueAsync(state));
        }
    }

    private async Task<JsonElement> PluginCommandAsync(string cwd, IReadOnlyList<string> arguments, bool installing, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellation.ThrowIfCancellationRequested();
        var start = await CodexInstallation.PrepareAsync(controller.State.CodexExecutable, controller.Profile(ProfileId), controller.State.Profiles);
        cancellation.ThrowIfCancellationRequested();
        start.ArgumentList.RemoveAt(start.ArgumentList.Count - 1); // Replace app-server with the supported CLI command.
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.WorkingDirectory = FolderPaths.Canonicalize(cwd);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        lifetime.CancelAfter(TimeSpan.FromMinutes(installing ? 5 : 2));
        using var process = Process.Start(start) ?? throw new IOException("Could not start Codex's plugin command.");
        process.StandardInput.Close();
        try
        {
            var output = ReadCommandOutputAsync(process.StandardOutput, 8 * 1024 * 1024, lifetime.Token);
            var errors = ReadCommandOutputAsync(process.StandardError, 64 * 1024, lifetime.Token);
            await process.WaitForExitAsync(lifetime.Token);
            var json = await output; var error = await errors;
            if (process.ExitCode != 0) throw new IOException(Wire.Short(error.Length > 0 ? error.Trim() : "Codex's plugin command failed.", 2000));
            try { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
            catch (JsonException) { throw new IOException(installing ? "Codex returned an unreadable install result. Refresh installed extensions before retrying." : "Codex returned an unreadable plugin catalog."); }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new IOException(installing ? "The install command timed out. Its outcome is unknown; refresh installed extensions before retrying." : "Loading the plugin catalog timed out. Try Refresh.");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
    private static async Task<string> ReadCommandOutputAsync(StreamReader reader, int limit, CancellationToken cancellation)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; var oversized = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellation)) > 0)
        {
            var keep = Math.Min(read, limit - result.Length);
            if (keep > 0) result.Append(buffer, 0, keep);
            oversized |= keep < read;
        }
        if (oversized) throw new IOException("Codex's plugin response exceeded the display limit. Refresh before retrying an installation.");
        return result.ToString();
    }
}
