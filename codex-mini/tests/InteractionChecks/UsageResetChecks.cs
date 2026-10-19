using System.Reflection;
using System.Text.Json;
using Windows.UI.Notifications;
using MiniCodex.Codex;
using MiniCodex.Services;

internal static partial class Program
{
    private static void CheckUsageResetNotifications(AppController controller, WindowManager windows, Guid profileId)
    {
        var clock = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var tracker = new RateLimitResetTracker();
        UsageLimitReport Report(double? primary, double? secondary, int minute, int next = 60, long primaryDuration = 300) => new(
            primary is { } p ? new(p, clock.AddMinutes(next), primaryDuration) : null,
            secondary is { } s ? new(s, clock.AddDays(7), 10080) : null, clock.AddMinutes(minute));
        Expect(tracker.Observe(profileId, "first-login", Report(100, 80, 0)) is null, "Initial usage snapshot is silent");
        Expect(tracker.Observe(profileId, "first-login", Report(100, 80, 61)) is null, "Countdown expiry alone does not announce a reset");
        var reset = tracker.Observe(profileId, "first-login", Report(0, 80, 62, 360));
        Expect(reset is { Primary: not null, Secondary: null }, "Confirmed short-term quota reset is detected");
        Expect(reset!.Description.Contains("5-hour limit reset") && reset.Description.Contains("100% remaining"), "Reset notification describes the actual window and remaining usage");
        Expect(tracker.Observe(profileId, "first-login", Report(0, 80, 63, 360)) is null, "Polling the same reset does not notify twice");
        Expect(tracker.Observe(profileId, "first-login", Report(30, 80, 64, 360)) is null, "Ordinary usage does not notify");
        Expect(tracker.Observe(profileId, "first-login", Report(29, 79, 65, 360)) is null, "Small drops before expiry do not look like quota resets");
        reset = tracker.Observe(profileId, "first-login", Report(0, 0, 66, 360));
        Expect(reset is { Primary: not null, Secondary: not null }, "Simultaneous window resets are combined");
        Expect(reset!.Description.Contains("Weekly limit reset"), "Weekly reset is identified separately");
        Expect(tracker.Observe(profileId, "new-login", Report(90, 70, 67)) is null, "Changing account scope creates a silent baseline");
        Expect(tracker.Observe(profileId, "new-login", Report(null, null, 68)) is null, "Missing quota data does not notify");
        Expect(tracker.Observe(profileId, "new-login", Report(0, null, 69)) is { Primary: not null }, "A confirmed reset after missing data is detected");
        Expect(tracker.Observe(profileId, "new-login", Report(100, 90, 10)) is null, "An older response cannot replace the newer baseline");
        Expect(tracker.Observe(profileId, "new-login", Report(0, 70, 70, primaryDuration: 600)) is null, "A changed quota duration establishes a new baseline");
        tracker.Forget(profileId);
        Expect(tracker.Observe(profileId, "new-login", Report(0, 0, 71)) is null, "Sign-out or home change clears the baseline");
        var other = Guid.NewGuid();
        Expect(tracker.Observe(other, "another-account", Report(25, null, 0)) is null, "Another account starts independently");
        Expect(tracker.Observe(other, "another-account", Report(40, null, 61, 360)) is not null, "Advanced expired window detects a reset even when new work already consumed usage");

        Equal(clock.AddMinutes(10), Report(20, null, 0).NextCheckAt, "Ordinary idle checks wait ten minutes");
        Equal(clock.AddMinutes(2).AddSeconds(2), Report(20, null, 0, next: 2).NextCheckAt, "A near reset schedules an earlier confirmation");
        Equal(clock.AddMinutes(3), Report(20, null, 2, next: 1).NextCheckAt, "Unconfirmed expiry schedules a one-minute follow-up");
        using (var data = JsonDocument.Parse("{\"primary\":{\"usedPercent\":\"unknown\",\"resetsAt\":1},\"secondary\":null}"))
            Expect(UsageLimitReport.Parse(data.RootElement, clock).Primary is null, "Malformed usage is not treated as zero");
        using (var data = JsonDocument.Parse("{\"primary\":{\"usedPercent\":20,\"resetsAt\":253402300799}}"))
            Equal(clock.AddMinutes(10), UsageLimitReport.Parse(data.RootElement, clock).NextCheckAt, "Far-future server timestamps cannot overflow the schedule");

        var secondId = controller.AddProfile("Reset notification second account");
        var firstRuntime = windows.Runtimes.Get(profileId); var secondRuntime = windows.Runtimes.Get(secondId);
        void Authenticate(AccountRuntime runtime, string identity)
        {
            typeof(AccountRuntime).GetProperty(nameof(AccountRuntime.SignedIn))!.SetValue(runtime, true);
            typeof(AccountRuntime).GetProperty(nameof(AccountRuntime.Identity))!.SetValue(runtime, identity);
            typeof(AccountRuntime).GetProperty(nameof(AccountRuntime.Authentication))!.SetValue(runtime, new AccountAuthentication(true, identity, DateTimeOffset.UtcNow));
        }
        void Apply(AccountRuntime runtime, double primary, double secondary, long credits, bool partial = false, string limitId = "codex")
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                rateLimits = new { limitId, primary = new { usedPercent = primary, resetsAt = DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeSeconds(), windowDurationMins = 300 },
                    secondary = new { usedPercent = secondary, resetsAt = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds(), windowDurationMins = 10080 } },
                rateLimitResetCredits = new { availableCount = credits }
            }));
            typeof(AccountRuntime).GetMethod("ApplyUsage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(runtime, [document.RootElement.Clone(), partial]);
        }
        Authenticate(firstRuntime, "first@example.invalid"); Authenticate(secondRuntime, "second@example.invalid");
        var notifications = new List<ToastNotification>(); var opened = new List<Guid>();
        using (var service = new NotificationService(controller, windows, notifications.Add, monitorAccounts: false, openAccount: id => { opened.Add(id); return Task.CompletedTask; }))
        {
            Apply(firstRuntime, 100, 50, 1); Apply(secondRuntime, 40, 100, 2);
            Equal(0, notifications.Count, "Initial snapshots across accounts produce no Windows notification");
            Apply(firstRuntime, 100, 50, 3);
            Equal(0, notifications.Count, "New reset credits do not notify when usage is unchanged");
            using (var creditUpdate = JsonDocument.Parse("{\"rateLimitResetCredits\":{\"availableCount\":4}}"))
                typeof(AccountRuntime).GetMethod("ApplyUsage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(firstRuntime, [creditUpdate.RootElement.Clone(), true]);
            Equal(0, notifications.Count, "A credits-only push does not create a usage reset notification");
            Equal(4L, firstRuntime.AvailableResetCount, "Credits-only push still updates the reset button count");
            Apply(firstRuntime, 0, 50, 3);
            Equal(1, notifications.Count, "Successful usage read publishes a quota-reset notification");
            Apply(firstRuntime, 0, 50, 3, partial: true);
            Equal(1, notifications.Count, "Push and polling do not duplicate notifications");
            Apply(secondRuntime, 40, 0, 2, partial: true);
            Equal(2, notifications.Count, "Another account's pushed weekly reset also notifies");
            var xml = notifications[1].Content.GetXml();
            Expect(xml.Contains("Reset notification second account") && xml.Contains("Weekly limit reset") && xml.Contains("Open account"), "Notification names the originating account and reset window");
            var arguments = notifications[1].Content.DocumentElement.GetAttribute("launch");
            service.ActivateAsync(arguments, new Dictionary<string, string>()).GetAwaiter().GetResult();
            Equal(secondId, opened.Single(), "Clicking reset notification opens the originating account");
            service.ActivateAsync(arguments.Replace(secondId.ToString("D"), profileId.ToString("D")), new Dictionary<string, string>()).GetAwaiter().GetResult();
            Equal(1, opened.Count, "A live notification cannot be redirected to another account");
            Apply(firstRuntime, 100, 100, 3, partial: true, limitId: "another-bucket");
            Apply(firstRuntime, 0, 0, 3, partial: true, limitId: "another-bucket");
            Equal(2, notifications.Count, "Other metered buckets do not produce Codex reset alerts");
            windows.Runtimes.ForgetAuthentication(secondId);
            Apply(secondRuntime, 0, 0, 2);
            Equal(2, notifications.Count, "Reconnected account baseline does not produce an initial reset alert");
        }
        // Exercise all-account scheduling without starting any Codex processes or sending real toasts.
        var independentRegistry = new RuntimeRegistry(controller, windows.Drafts);
        var checkedProfiles = new List<Guid>();
        using (var monitor = new AccountUsageMonitor(controller, independentRegistry, _ => false, id => { checkedProfiles.Add(id); return Task.CompletedTask; }))
        {
            monitor.CheckDueAsync().GetAwaiter().GetResult();
            Expect(checkedProfiles.Contains(profileId) && checkedProfiles.Contains(secondId), "Accounts are checked even when no chat windows are open");
            Equal(0, independentRegistry.All.Count(), "Injected scheduler checks do not start background servers");
            var total = checkedProfiles.Count;
            monitor.CheckDueAsync().GetAwaiter().GetResult();
            Equal(total, checkedProfiles.Count, "Accounts are not polled again before their next check");
            monitor.Dispose(); monitor.CheckDueAsync().GetAwaiter().GetResult();
            Equal(total, checkedProfiles.Count, "Disposed monitor stops checking accounts");
        }
        foreach (var runtime in new[] { firstRuntime, secondRuntime })
            typeof(AccountRuntime).GetProperty(nameof(AccountRuntime.SignedIn))!.SetValue(runtime, false);
        controller.RemoveProfile(secondId);
    }
}
