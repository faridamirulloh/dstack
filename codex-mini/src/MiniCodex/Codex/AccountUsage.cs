using System.Text.Json;

namespace MiniCodex.Codex;

public sealed partial class AccountRuntime
{
    private long _usageRevision;
    public UsageLimitReport? LastLimitReport { get; private set; }
    internal string UsageScope => HomePath + "\n" + Identity + "\n" + _plan;
    public event Action<UsageLimitReport>? UsageObserved;

    private void ApplyUsage(JsonElement data, bool partial = false)
    {
        var limits = data.Get("rateLimitsByLimitId").Get("codex");
        if (limits.ValueKind != JsonValueKind.Object) limits = data.Get("rateLimits");
        var limitId = limits.Text("limitId");
        if (limits.ValueKind == JsonValueKind.Object && (limitId is null || limitId == "codex")) _rateLimits = limits;
        else if (!partial) _rateLimits = default;
        if (!partial || data.Get("rateLimitResetCredits").ValueKind != JsonValueKind.Undefined) _availableResetCount = ResetCount(data);
        _usageUpdated = DateTimeOffset.Now; _usageRevision++;
        // Other metered buckets and missing windows must not look like a Codex quota reset.
        if (limits.ValueKind != JsonValueKind.Object || limitId is not (null or "codex")) return;
        LastLimitReport = UsageLimitReport.Parse(_rateLimits, _usageUpdated.Value);
        UsageObserved?.Invoke(LastLimitReport);
    }
}
