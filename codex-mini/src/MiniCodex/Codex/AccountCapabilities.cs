using System.Text.Json;
using MiniCodex.Models;
using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    public event EventHandler? CapabilitiesChanged;
    private readonly Dictionary<string, ComposerReference> _mentionEntries = new(StringComparer.OrdinalIgnoreCase);
    public int MentionVersion { get; private set; }
    public IEnumerable<ComposerReference> MentionEntries => _mentionEntries.Values;
    private void RememberMentions(IEnumerable<ComposerReference> entries)
    {
        var changed = false;
        foreach (var entry in entries) if (!_mentionEntries.TryGetValue(entry.Path, out var previous) || previous != entry) { _mentionEntries[entry.Path] = entry; changed = true; }
        while (_mentionEntries.Count > 1000) _mentionEntries.Remove(_mentionEntries.Keys.First());
        if (changed) MentionVersion++;
    }

    public async Task<CapabilitySnapshot> DiscoverCapabilitiesAsync(ProjectState project, string? threadId, bool forceReload, CancellationToken cancellation)
    {
        if (!Connected || !SignedIn) return new([], "Connect and sign in to discover this account's skills and plugins.");
        var connection = Client;
        async Task<JsonElement> CallAsync(string method, object parameters)
        {
            cancellation.ThrowIfCancellationRequested();
            var response = await connection.CallAsync(method, parameters);
            cancellation.ThrowIfCancellationRequested();
            return response;
        }
        var entries = new List<ComposerReference>();
        var notices = new List<string>();
        var primaryPlugins = new HashSet<string>();
        try
        {
            var result = await CallAsync("skills/list", new { cwds = project.Folders, forceReload });
            foreach (var cwd in result.Get("data").Items())
            {
                foreach (var skill in cwd.Get("skills").Items())
                {
                    if (skill.Text("name") is not { Length: > 0 } name || skill.Text("path") is not { Length: > 0 } path) continue;
                    var plugin = skill.Text("pluginId");
                    if (plugin is not null && FolderPaths.Comparer.Equals(cwd.Text("cwd"), project.PrimaryFolder) && skill.Get("enabled").ValueKind == JsonValueKind.True) primaryPlugins.Add(plugin);
                    var source = plugin ?? skill.Text("scope") ?? "";
                    entries.Add(new(ReferenceKind.Skill, name, path, skill.Get("interface").Text("shortDescription") ?? skill.Text("description") ?? "",
                        skill.Get("enabled").ValueKind == JsonValueKind.True, source, plugin));
                }
                foreach (var error in cwd.Get("errors").Items()) notices.Add("Skill: " + (error.Text("message") ?? "Discovery failed") + " · " + error.Text("path"));
            }
        }
        catch (Exception error) when (error is IOException or TimeoutException) { notices.Add("Skills: " + error.Message); }

        // Plugin IDs from effective config also cover packages with only MCP tools.
        // Catalog/install RPCs are under development and are intentionally not used.
        foreach (var folder in new[] { project.PrimaryFolder })
        {
            try
            {
                var result = await CallAsync("config/read", new { includeLayers = false, cwd = folder });
                var plugins = result.Get("config").Get("plugins");
                if (plugins.ValueKind != JsonValueKind.Object) continue;
                foreach (var plugin in plugins.EnumerateObject())
                {
                    if (!plugin.Name.Contains('@')) continue;
                    entries.Add(new(ReferenceKind.Plugin, plugin.Name.Split('@')[0], "plugin://" + plugin.Name,
                        "Configured in " + System.IO.Path.GetFileName(folder), plugin.Value.Get("enabled").ValueKind == JsonValueKind.True, plugin.Name, plugin.Name));
                }
            }
            catch (Exception error) when (error is IOException or TimeoutException) { notices.Add("Plugin configuration: " + error.Message); }
        }
        foreach (var group in entries.Where(entry => entry.Kind == ReferenceKind.Skill && entry.PluginId is not null).GroupBy(entry => entry.PluginId!).ToArray())
        {
            entries.Add(new(ReferenceKind.Plugin, group.Key.Split('@')[0], "plugin://" + group.Key,
                primaryPlugins.Contains(group.Key) ? "Discovered plugin skills" : "Skills found in another project folder; mention a skill directly", primaryPlugins.Contains(group.Key), group.Key, group.Key));
        }

        try
        {
            string? cursor = null;
            var cursors = new HashSet<string>();
            for (var page = 0; page < 10; page++)
            {
                var result = await CallAsync("app/list", new { cursor, limit = 50, threadId, forceRefetch = forceReload && page == 0 });
                foreach (var app in result.Get("data").Items())
                {
                    if (app.Text("id") is not { Length: > 0 } id || app.Text("name") is not { Length: > 0 } name) continue;
                    var enabled = app.Get("isAccessible").ValueKind == JsonValueKind.True && app.Get("isEnabled").ValueKind == JsonValueKind.True;
                    entries.Add(new(ReferenceKind.Connector, name, "app://" + id, app.Text("description") ?? "", enabled, enabled ? "Available" : "Not connected or disabled"));
                }
                cursor = result.Text("nextCursor");
                if (string.IsNullOrEmpty(cursor)) break;
                if (!cursors.Add(cursor) || page == 9) { notices.Add("Connector list is limited to 500 entries."); break; }
            }
        }
        catch (Exception error) when (error is IOException or TimeoutException)
        {
            var denied = error.Message.Contains("403", StringComparison.OrdinalIgnoreCase);
            notices.Add(denied ? "The connector directory denied access (403). Showing installed connectors where available; plugins and skills remain usable."
                : "Connector directory unavailable. Showing installed connectors where available.");
            try
            {
                var installed = await CallAsync("app/installed", new { threadId, forceRefresh = false });
                foreach (var app in installed.Get("apps").Items())
                {
                    if (app.Text("id") is not { Length: > 0 } id) continue;
                    var enabled = app.Get("enabled").ValueKind == JsonValueKind.True && app.Get("callable").ValueKind == JsonValueKind.True;
                    entries.Add(new(ReferenceKind.Connector, app.Text("runtimeName") ?? id, "app://" + id,
                        "Installed connector", enabled, enabled ? "Installed" : "Disabled or unavailable"));
                }
            }
            catch (Exception fallback) when (fallback is IOException or TimeoutException)
            { notices.Add("Installed connectors are also unavailable. Retry after reconnecting this account."); }
        }
        // Use the descriptions of a plugin's own skills when no plugin summary is exposed.
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Kind != ReferenceKind.Plugin) continue;
            var descriptions = entries.Where(skill => skill.Kind == ReferenceKind.Skill && skill.PluginId == entry.PluginId && skill.Description.Length > 0)
                .Select(skill => skill.Name + ": " + skill.Description).Distinct().Take(6).ToArray();
            if (descriptions.Length > 0) entries[index] = entry with { Description = string.Join("\n", descriptions) };
        }
        RememberMentions(entries);
        return new(entries.GroupBy(entry => (entry.Kind, entry.Path)).Select(group => group.OrderByDescending(entry => entry.Enabled).First())
            .OrderBy(entry => entry.Kind).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray(), string.Join("\n", notices.Distinct()));
    }

    private object[] BuildInput(string text, IReadOnlyList<ComposerReference> references, IReadOnlyList<string> folders, IReadOnlyList<ChatAttachment> attachments)
    {
        RememberMentions(references);
        var input = new List<object> { new { type = "text", text } };
        foreach (var attachment in attachments.Where(attachment => attachment.IsImage))
        {
            if (!File.Exists(attachment.Path)) throw new IOException("Attached image is unavailable: " + attachment.Name);
            input.Add(new { type = "localImage", path = attachment.Path });
        }
        foreach (var reference in references.DistinctBy(reference => (reference.Kind, reference.Path)))
        {
            if (!reference.Enabled || !reference.IsInText(text)) continue;
            if (reference.Kind is ReferenceKind.File or ReferenceKind.Folder) ProjectFiles.Validate(reference.Path, folders);
            else if (reference.Kind == ReferenceKind.Skill) input.Add(new { type = "skill", name = reference.Name, path = reference.Path });
            else if (reference.Kind == ReferenceKind.Connector) input.Add(new { type = "mention", name = reference.Name, path = reference.Path });
            // Plugins use Codex's documented [@name](plugin://id) prompt marker.
        }
        return input.ToArray();
    }
}
