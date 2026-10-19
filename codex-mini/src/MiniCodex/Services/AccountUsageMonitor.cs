using System.Windows.Threading;
using MiniCodex.Codex;

namespace MiniCodex.Services;

public sealed class AccountUsageMonitor(AppController controller, RuntimeRegistry runtimes, Func<Guid, bool> hasWindow, Func<Guid, Task>? checkProfile = null) : IDisposable
{
    private sealed class Schedule(string home)
    {
        public string Home { get; } = home;
        public DateTimeOffset Seen { get; set; }
        public DateTimeOffset Next { get; set; }
    }
    private readonly Dictionary<Guid, Schedule> _checks = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _checking;
    private bool _started;
    private bool _disposed;

    public void Start() { if (_started || _disposed) return; _started = true; _timer.Tick += Tick; _timer.Start(); }
    private async void Tick(object? sender, EventArgs e)
    {
        _timer.Interval = TimeSpan.FromMinutes(1);
        await CheckDueAsync();
    }

    public async Task CheckDueAsync()
    {
        if (_disposed || _checking || runtimes.IsStopping) return;
        _checking = true;
        try
        {
            var profiles = controller.State.Profiles.ToArray();
            var keep = profiles.Select(profile => profile.Id).ToHashSet();
            foreach (var removed in _checks.Keys.Where(id => !keep.Contains(id)).ToArray()) _checks.Remove(removed);
            foreach (var profile in profiles)
            {
                if (_disposed || runtimes.IsStopping) break;
                if (runtimes.IsRemoving(profile.Id) || runtimes.Authentication(profile.Id) is { SignedIn: false }
                    || runtimes.Find(profile.Id) is { LoginPending: true } or { HasWork: true, Connected: false }) continue;
                var home = CodexInstallation.Home(profile);
                if (!_checks.TryGetValue(profile.Id, out var schedule) || !schedule.Home.Equals(home, StringComparison.OrdinalIgnoreCase))
                    _checks[profile.Id] = schedule = new(home);
                if (runtimes.LimitReport(profile.Id) is { } known && known.ObservedAt > schedule.Seen)
                { schedule.Seen = known.ObservedAt; schedule.Next = known.NextCheckAt; }
                if (schedule.Next > DateTimeOffset.UtcNow) continue;
                // Failures back off; a confirmed due window can request a sooner follow-up.
                schedule.Next = DateTimeOffset.UtcNow.AddMinutes(10);
                await (checkProfile ?? CheckProfileAsync)(profile.Id);
                if (runtimes.LimitReport(profile.Id) is { } latest && latest.ObservedAt > schedule.Seen)
                { schedule.Seen = latest.ObservedAt; schedule.Next = latest.NextCheckAt; }
            }
        }
        finally { _checking = false; }
    }

    private async Task CheckProfileAsync(Guid profileId)
    {
        RuntimeRegistry.RuntimeLease? lease = null;
        var wasSleeping = runtimes.Find(profileId)?.Suspended == true;
        try
        {
            lease = await runtimes.BorrowAsync(profileId);
            if (_disposed || runtimes.IsStopping || runtimes.IsRemoving(profileId)) return;
            if (!lease.Runtime.Connected) await lease.Runtime.ConnectAsync(false, recordActivity: false);
            if (lease.Runtime.SignedIn) await lease.Runtime.RefreshUsageAsync();
        }
        catch (Exception error) { if (hasWindow(profileId)) runtimes.Find(profileId)?.Report(error); }
        finally
        {
            if (lease is not null)
            {
                try
                {
                    try
                    {
                        if (!hasWindow(profileId) && !lease.Runtime.HasWork) await runtimes.ReleaseAsync(profileId);
                        else if (wasSleeping) await lease.Runtime.SuspendIfIdleAsync();
                    }
                    finally { await lease.DisposeAsync(); }
                }
                catch (Exception error) { if (hasWindow(profileId)) runtimes.Find(profileId)?.Report(error); }
            }
        }
    }

    public void Dispose() { _disposed = true; _timer.Stop(); _timer.Tick -= Tick; }
}
