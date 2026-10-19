using System.Text.Json;
using MiniCodex.Models;
using MiniCodex.Codex;

namespace MiniCodex.Services;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string FilePath { get; }
    public StateStore(string? filePath = null) => FilePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCodex", "ui-state.json");

    public AppState Load()
    {
        if (!File.Exists(FilePath))
        {
            var initial = new AppState { Profiles = [new ProfileState()] };
            Save(initial);
            return initial;
        }
        try
        {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath), JsonOptions)
                ?? throw new InvalidDataException("Settings are empty.");
            Validate(state);
            ProjectMatching.RepairSavedProjects(state);
            return state;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidDataException($"Cannot load Mini Codex settings at {FilePath}. The file has been preserved. {error.Message}", error);
        }
    }

    public AppState Copy(AppState state) => JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, JsonOptions), JsonOptions)!;

    public void Save(AppState state)
    {
        Validate(state);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
            else File.Move(temporary, FilePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(AppState state)
    {
        if (state.Version != 1) throw new InvalidDataException($"Unsupported settings version {state.Version}.");
        if (!double.IsFinite(state.UiZoom) || state.UiZoom < 0.75 || state.UiZoom > 1.5) throw new InvalidDataException("UI zoom must be between 75% and 150%.");
        if (state.Profiles is null) throw new InvalidDataException("The account profile list is missing.");
        if (state.AccountWindowLayouts is null || state.AccountWindowLayouts.Any(layout => layout is null || layout.Any(id => id == Guid.Empty)))
            throw new InvalidDataException("The saved account window layout is invalid.");
        var profileIds = new HashSet<Guid>();
        var homes = new HashSet<string>(FolderPaths.Comparer);
        foreach (var profile in state.Profiles)
        {
            if (profile is null || profile.Id == Guid.Empty || !profileIds.Add(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) || profile.Projects is null)
                throw new InvalidDataException("A profile has invalid settings.");
            if (profile.BusySendMode is not ("Steer" or "Queue" or "Ask")) throw new InvalidDataException("Unsupported send-while-running preference.");
            if (profile.CustomProjectOrder is null || profile.CustomProjectOrder.Any(string.IsNullOrWhiteSpace) || profile.CustomProjectOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count() != profile.CustomProjectOrder.Count)
                throw new InvalidDataException("Invalid custom project order.");
            var home = CodexInstallation.Home(profile);
            if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home) || !homes.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(home))))
                throw new InvalidDataException("Account profiles must use distinct absolute Codex homes.");
            if (!new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" }.Contains(profile.Effort.ToLowerInvariant()) || profile.ApprovalMode is not ("Ask me" or "Auto approval"))
                throw new InvalidDataException("An account has an unsupported UI preference.");
            var projectIds = new HashSet<Guid>();
            foreach (var project in profile.Projects)
            {
                if (project is null || project.Id == Guid.Empty || !projectIds.Add(project.Id) || string.IsNullOrWhiteSpace(project.Name) || project.Folders is null || project.Chats is null)
                    throw new InvalidDataException("A project has invalid settings.");
                if (project.Folders.Count == 0 || project.Folders.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) || project.Folders.Distinct(FolderPaths.Comparer).Count() != project.Folders.Count || !project.Folders.Contains(project.PrimaryFolder, FolderPaths.Comparer))
                    throw new InvalidDataException($"Project {project.Name} needs distinct absolute folders and a primary folder from that list.");
                if (project.ExcludedCodexFolders is null || project.ExcludedCodexFolders.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
                    throw new InvalidDataException($"Project {project.Name} has invalid excluded folders.");
                var chatIds = new HashSet<Guid>();
                if (project.Chats.Any(chat => chat is null || chat.Id == Guid.Empty || !chatIds.Add(chat.Id) || string.IsNullOrWhiteSpace(chat.Name)))
                    throw new InvalidDataException("A local chat has invalid settings.");
                if (project.Chats.Any(chat => chat.ConflictPause is { } pause && (pause.Files is null || pause.Files.Count == 0
                    || pause.Files.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) || pause.Blockers is null
                    || pause.Blockers.Count == 0 || pause.Blockers.Any(blocker => blocker is null || blocker.ProfileId == Guid.Empty
                        || blocker.ChatId == Guid.Empty || blocker.TaskRunId == Guid.Empty || string.IsNullOrWhiteSpace(blocker.Name)))))
                    throw new InvalidDataException("A paused task has invalid file-conflict settings.");
            }
        }
    }
}
