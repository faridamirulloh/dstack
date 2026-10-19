using MiniCodex.Services;
using System.Text.Json;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    private DateTimeOffset _lastUse = DateTimeOffset.UtcNow;
    private bool _suspending;
    public bool Suspended { get; private set; }
    public void Touch() => _lastUse = DateTimeOffset.UtcNow;
    internal async Task SuspendIfIdleAsync()
    {
        if (_suspending || _disposed || !Connected || HasWork || HasQueuedMessages || LoginPending || Approvals.Count > 0
            || _refreshingUsage || _client?.PendingCount != 0 || DateTimeOffset.UtcNow - _lastUse < TimeSpan.FromMinutes(10)) return;
        _suspending = true;
        await _connect.WaitAsync();
        try
        {
            if (!Connected || HasWork || HasQueuedMessages || LoginPending || Approvals.Count > 0 || _client?.PendingCount != 0
                || DateTimeOffset.UtcNow - _lastUse < TimeSpan.FromMinutes(10)) return;
            try { if (!await LoadedThreadsAreIdleAsync()) return; }
            catch (Exception error) { Touch(); Report(new IOException("Memory saver kept the connection open because it could not confirm background work was idle. " + error.Message)); return; }
            if (HasWork || HasQueuedMessages || LoginPending || Approvals.Count > 0 || _client?.PendingCount != 0 || DateTimeOffset.UtcNow - _lastUse < TimeSpan.FromMinutes(10)) return;
            drafts.Flush();
            var previous = _client; _client = null; _initialized = false;
            _usagePollTimer?.Stop();
            if (previous is not null) await previous.DisposeAsync();
            foreach (var state in _conversations.Values.Where(state => !_pinnedConversations.ContainsKey(state.LocalId)))
            { state.Messages.Clear(); state.Transcript.Clear(); state.Loaded = false; state.HistoryCursor = null; }
            Suspended = true; Status = "Sleeping · reconnect on use"; Notify();
        }
        finally { _suspending = false; _connect.Release(); }
    }
    private async Task<bool> LoadedThreadsAreIdleAsync()
    {
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var loaded = await Client.CallAsync("thread/loaded/list", new { cursor, limit = 100 });
            if (loaded.Get("data").ValueKind != JsonValueKind.Array) return false;
            foreach (var thread in loaded.Get("data").Items())
            {
                if (HasWork || HasQueuedMessages || LoginPending || Approvals.Count > 0 || DateTimeOffset.UtcNow - _lastUse < TimeSpan.FromMinutes(10)) return false;
                if (thread.Text() is not { } id) return false;
                var metadata = await Client.CallAsync("thread/read", new { threadId = id, includeTurns = false });
                if (metadata.Get("thread").Get("status").Text("type") is not ("idle" or "notLoaded")) return false;
                var background = await Client.CallAsync("thread/backgroundTerminals/list", new { threadId = id, limit = 1 });
                if (background.Get("data").ValueKind != JsonValueKind.Array || background.Get("data").Items().Any() || background.Text("nextCursor") is not null) return false;
            }
            cursor = loaded.Text("nextCursor"); if (cursor is null) return true;
        }
        return false;
    }
}
