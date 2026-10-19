using MiniCodex.Models;
using MiniCodex.Services;

namespace MiniCodex.Codex;

public sealed partial class RuntimeRegistry
{
    private bool _refreshingFileConflictWaits;
    private IEnumerable<(AccountRuntime Runtime, ConversationState State)> ActiveFileTasks() => _accounts.Values
        .SelectMany(runtime => runtime.Conversations.Where(state => state.Busy || state.Uncertain)
            .Select(state => (runtime, state)));

    private bool IsFileConflictBlocked(FileConflictPause pause) => ActiveFileTasks().Any(task =>
        pause.Blockers.Any(blocker => blocker.ProfileId == task.Runtime.ProfileId && blocker.ChatId == task.State.LocalId && blocker.TaskRunId == task.State.TaskRunId)
        || pause.Files.Any(task.State.EditedFiles.Contains));

    private void RefreshFileConflictWaits()
    {
        if (_refreshingFileConflictWaits) return;
        _refreshingFileConflictWaits = true;
        try { foreach (var runtime in _accounts.Values.ToArray()) runtime.RefreshConflictAvailability(); }
        finally { _refreshingFileConflictWaits = false; }
    }
    private void OnFilesEdited(AccountRuntime runtime, ConversationState state, IReadOnlyList<string> files)
    {
        // Claims last until the turn has stopped or finished. Two accounts and two
        // projects referring to the same physical folder participate in one tracker.
        foreach (var other in ActiveFileTasks().Where(task => !ReferenceEquals(task.State, state)).ToArray())
        {
            var overlap = files.Where(other.State.EditedFiles.Contains).Distinct(FolderPaths.Comparer).ToArray();
            if (overlap.Length == 0) continue;
            var order = Nullable.Compare(state.TaskStartedAt, other.State.TaskStartedAt);
            if (order == 0) order = state.TaskRunId.CompareTo(other.State.TaskRunId);
            var newerRuntime = order > 0 ? runtime : other.Runtime;
            var newer = order > 0 ? state : other.State;
            var olderRuntime = order > 0 ? other.Runtime : runtime;
            var older = order > 0 ? other.State : state;
            var profile = controller.Profile(olderRuntime.ProfileId);
            var name = profile.Projects.SelectMany(project => project.Chats).FirstOrDefault(chat => chat.Id == older.LocalId)?.Name ?? "Task";
            newerRuntime.PauseForFileConflict(newer, overlap, new(olderRuntime.ProfileId, older.LocalId, older.TaskRunId, profile.Name + " / " + name));
        }
        RefreshFileConflictWaits();
    }
}
