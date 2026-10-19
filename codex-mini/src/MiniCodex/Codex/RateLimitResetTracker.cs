using System.Text.Json;

namespace MiniCodex.Codex;

public sealed record UsageLimitWindow(double UsedPercent, DateTimeOffset? ResetsAt, long? DurationMinutes)
{
    public static UsageLimitWindow? Parse(JsonElement data)
    {
        var used = data.Get("usedPercent");
        if (used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out var percent) || !double.IsFinite(percent) || percent < 0) return null;
        var timestamp = data.Get("resetsAt").Number();
        var reset = timestamp is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(timestamp.Value) : (DateTimeOffset?)null;
        var duration = data.Get("windowDurationMins").Number();
        return new(Math.Min(percent, 100), reset, duration is > 0 ? duration : null);
    }

    public string Label(bool primary) => DurationMinutes switch
    {
        10080 => "Weekly limit", 1440 => "Daily limit",
        > 0 when DurationMinutes % 60 == 0 => $"{DurationMinutes / 60}-hour limit",
        > 0 => $"{DurationMinutes}-minute limit",
        _ => primary ? "Short-term limit" : "Long-term limit"
    };
}

public sealed record UsageLimitReport(UsageLimitWindow? Primary, UsageLimitWindow? Secondary, DateTimeOffset ObservedAt)
{
    public static UsageLimitReport Parse(JsonElement limits, DateTimeOffset observedAt) => new(
        UsageLimitWindow.Parse(limits.Get("primary")), UsageLimitWindow.Parse(limits.Get("secondary")), observedAt);

    public DateTimeOffset NextCheckAt
    {
        get
        {
            var next = ObservedAt.AddMinutes(10);
            foreach (var window in new[] { Primary, Secondary })
                if (window is { UsedPercent: > 0, ResetsAt: { } reset })
                {
                    // A due timestamp schedules a check; only a server observation can confirm a reset.
                    var check = reset > ObservedAt ? (reset < next ? reset.AddSeconds(2) : next) : ObservedAt.AddMinutes(1);
                    if (check < next) next = check;
                }
            return next;
        }
    }
}

public sealed record UsageLimitReset(UsageLimitWindow? Primary, UsageLimitWindow? Secondary)
{
    public string Description => string.Join("\n", new[]
    {
        Primary is { } primary ? primary.Label(true) + $" reset ({100 - primary.UsedPercent:0.#}% remaining)." : "",
        Secondary is { } secondary ? secondary.Label(false) + $" reset ({100 - secondary.UsedPercent:0.#}% remaining)." : ""
    }.Where(text => text.Length > 0));
}

public sealed record UsageLimitWarning(UsageLimitWindow? Primary, UsageLimitWindow? Secondary)
{
    public bool Exhausted => Primary is { UsedPercent: >= 100 } || Secondary is { UsedPercent: >= 100 };
    public string Description => string.Join("\n", new[]
    {
        Describe(Primary, true), Describe(Secondary, false)
    }.Where(text => text.Length > 0));

    private static string Describe(UsageLimitWindow? window, bool primary) => window is null ? ""
        : window.Label(primary) + (window.UsedPercent >= 100 ? " exhausted (0% remaining)." : $" has only {100 - window.UsedPercent:0.#}% remaining.");
}

public sealed class RateLimitResetTracker
{
    private sealed record WindowState(UsageLimitWindow Window, bool LowNotified, bool ExhaustedNotified);
    private sealed record State(string Scope, WindowState? Primary, WindowState? Secondary, DateTimeOffset ObservedAt);
    private readonly Dictionary<Guid, State> _accounts = [];

    public void Forget(Guid profileId) => _accounts.Remove(profileId);

    public UsageLimitReset? Observe(Guid profileId, string scope, UsageLimitReport report) => Observe(profileId, scope, report, out _);

    public UsageLimitReset? Observe(Guid profileId, string scope, UsageLimitReport report, out UsageLimitWarning? warning)
    {
        warning = null;
        var previous = _accounts.GetValueOrDefault(profileId);
        if (previous is not null && previous.Scope == scope && report.ObservedAt < previous.ObservedAt) return null;
        var baseline = previous is not null && previous.Scope == scope ? previous : null;
        var primary = Reset(baseline?.Primary?.Window, report.Primary, report.ObservedAt);
        var secondary = Reset(baseline?.Secondary?.Window, report.Secondary, report.ObservedAt);
        var primaryState = ObserveWindow(baseline?.Primary, report.Primary, primary, out var primaryWarning);
        var secondaryState = ObserveWindow(baseline?.Secondary, report.Secondary, secondary, out var secondaryWarning);
        _accounts[profileId] = new(scope, primaryState, secondaryState, report.ObservedAt);
        if (primaryWarning is not null || secondaryWarning is not null) warning = new(primaryWarning, secondaryWarning);
        return primary || secondary ? new(primary ? report.Primary : null, secondary ? report.Secondary : null) : null;
    }

    private static WindowState? ObserveWindow(WindowState? before, UsageLimitWindow? after, bool reset, out UsageLimitWindow? warning)
    {
        warning = null;
        if (after is null) return before;
        if (before is null || reset
            || before.Window.DurationMinutes is { } oldDuration && after.DurationMinutes is { } duration && duration != oldDuration)
            return new(after, after.UsedPercent >= 90, after.UsedPercent >= 100);
        var low = after.UsedPercent >= 90;
        var exhausted = after.UsedPercent >= 100;
        if (exhausted && !before.ExhaustedNotified || low && !before.LowNotified) warning = after;
        return new(after, before.LowNotified || low, before.ExhaustedNotified || exhausted);
    }

    private static bool Reset(UsageLimitWindow? before, UsageLimitWindow? after, DateTimeOffset observedAt)
    {
        if (before is null || after is null || before.UsedPercent <= 0) return false;
        if (before.DurationMinutes is { } oldDuration && after.DurationMinutes is { } duration && duration != oldDuration) return false;
        if (after.UsedPercent == 0) return true;
        return before.ResetsAt is { } oldDue && observedAt >= oldDue
            && after.ResetsAt is { } newDue && newDue > oldDue && newDue > observedAt;
    }
}
