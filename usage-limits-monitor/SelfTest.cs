using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UsageLimitsMonitor
{
    internal static class SelfTest
    {
        private static readonly List<string> results = new List<string>();
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            results.Add("PASS " + name);
        }
        private static Snapshot Sample(double remaining, long reset, long at)
        {
            return new Snapshot { At = at, Quotas = new List<Quota> { new Quota { Key = "codex/primary", Bucket = "Codex", Window = "5 hour", Remaining = remaining, ResetsAt = reset } } };
        }
        public static int Main(string[] args)
        {
            string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
            Directory.CreateDirectory(root);
            string scratch = Path.Combine(root, "checks-" + Guid.NewGuid().ToString("N"));
            try
            {
                Run(scratch);
                if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])) Probe(args[1], scratch).GetAwaiter().GetResult();
                results.Add("All " + results.Count + " checks passed.");
                File.WriteAllLines(Path.Combine(root, "checks.txt"), results);
                Console.WriteLine(string.Join(Environment.NewLine, results));
                return 0;
            }
            catch (Exception error)
            {
                results.Add("FAIL " + error.Message);
                File.WriteAllLines(Path.Combine(root, "checks.txt"), results);
                Console.WriteLine(string.Join(Environment.NewLine, results));
                return 1;
            }
        }
        private static void Run(string scratch)
        {
            long now = Json.Now;
            var response = Json.Read(@"{""rateLimits"":{""primary"":{""usedPercent"":1}},""rateLimitsByLimitId"":{""codex"":{""limitName"":""Codex"",""primary"":{""usedPercent"":100,""windowDurationMins"":300,""resetsAt"":2000000000},""secondary"":{""usedPercent"":15,""windowDurationMins"":10080},""credits"":{""hasCredits"":true,""balance"":""42.50""}},""review"":{""primary"":{""usedPercent"":12}}}}");
            var parsed = Usage.Parse(response, now);
            Check(parsed.Quotas.Count == 3, "All buckets and windows retained; fallback not duplicated");
            Check(parsed.Quotas[0].Remaining == 0 && parsed.Quotas[1].Remaining == 85, "Exhausted and available windows stay separate");
            Check(parsed.Quotas[0].Credits == "42.5", "Credits retained after quota exhaustion");
            Check(parsed.Quotas[2].Credits == "Unavailable" && parsed.Quotas[2].ResetsAt == null, "Missing credits and reset stay unknown");
            Check(Usage.CreditText(Json.Read(@"{""balance"":0}")) == "0", "Explicit zero credit balance remains zero");
            Check(Usage.CreditText(Json.Read(@"{""unlimited"":true}")) == "Unlimited", "Unlimited credits are not a fabricated number");
            Check(Usage.CreditText(Json.Read(@"{""balance"":""NaN""}")) == "Unavailable", "Malformed balance rejected");
            var missing = Usage.Parse(Json.Read(@"{""rateLimits"":{""primary"": {""usedPercent"":null,""resetsAt"":-1},""secondary"":{""usedPercent"":-9}}}"), now);
            Check(missing.Quotas.All(q => !q.Remaining.HasValue) && missing.Quotas[0].ResetsAt == null, "Missing and negative percentages are unknown");
            Check(Usage.Parse(Json.Read("{}"), now).Quotas.Count == 0, "Absent quotas are not full capacity");
            var creditOnly = Usage.Parse(Json.Read(@"{""rateLimits"":{""credits"":{""balance"":7}}}"), now);
            Check(creditOnly.Quotas.Single().Remaining == null && creditOnly.Quotas.Single().Credits == "7", "Credit-only response keeps its balance without inventing a quota");
            Check(Usage.ResetText(new Quota { ResetsAt = now - 10 }, now) == "Awaiting confirmation", "Elapsed reset does not claim restored capacity");
            long tipReset = (long)(new DateTime(2026, 9, 3, 14, 32, 0, DateTimeKind.Local).ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            var tipAccount = new Account { Label = "F01", Last = Sample(70, tipReset, now) };
            tipAccount.Last.Quotas.Add(new Quota { Key = "codex/secondary", Remaining = 90, ResetsAt = tipReset + 86400 });
            Check(Usage.AccountTip(tipAccount, true, tipReset - 5 * 86400) == "F01: 70% / 5d / 3-Sep 14:32", "Tooltip uses the requested format and one lowest-quota row per account");
            Check(Usage.AccountTip(tipAccount, false, tipReset - 4 * 3600) == "F01: 70% / 4h / 3-Sep 14:32 (stale)", "Tooltip uses compact hours and labels saved data as stale");
            tipAccount.Last.Quotas[0].Remaining = 0;
            Check(Usage.AccountTip(tipAccount, true, tipReset + 1).Contains("0% / Awaiting confirmation"), "Tooltip does not invent a refill when the reset passes");
            tipAccount.Last.Quotas[0].ResetsAt = null;
            Check(Usage.AccountTip(tipAccount, true, now) == "F01: 0% / Not reported / Not reported" && Usage.AccountTip(new Account { Label = "Empty" }, false, now).StartsWith("Empty: Unavailable"), "Tooltip preserves missing reset and account data");
            var serviceUsage = Usage.ParseHistory(Json.Read(@"{""summary"":{""lifetimeTokens"":1234567,""peakDailyTokens"":45678},""dailyUsageBuckets"":[{""startDate"":""2026-08-25"",""tokens"":12345},{""startDate"":""2026-08-26"",""tokens"":0}]}"));
            Check(serviceUsage.LifetimeTokens == 1234567 && serviceUsage.Days.Count == 2 && serviceUsage.Days[0].Tokens == 0 && serviceUsage.Days[1].Tokens == 12345, "OpenAI history retains daily token counts, explicit zero, and date ordering");
            Check(Usage.ParseHistory(Json.Read("{}")).Days == null && Usage.ParseHistory(Json.Read(@"{""dailyUsageBuckets"":[]}")).Days.Count == 0, "Unavailable OpenAI history is distinct from an empty returned list");
            var invalidTokens = Usage.ParseHistory(Json.Read(@"{""summary"":{""lifetimeTokens"":-1},""dailyUsageBuckets"":[{""startDate"":""2026-08-26"",""tokens"":true}]}"));
            Check(invalidTokens.LifetimeTokens == null && invalidTokens.Days[0].Tokens == null, "Invalid service token counts do not become zero");
            bool invalidDate = false;
            try { Usage.ParseHistory(Json.Read(@"{""dailyUsageBuckets"":[{""startDate"":""2026-02-30"",""tokens"":1}]}")); } catch (InvalidDataException) { invalidDate = true; }
            Check(invalidDate, "Malformed service history dates are rejected");
            var today = DateTime.UtcNow.Date;
            var ranges = new ServiceUsage { Days = new[] { -30, -29, -7, -6, 0 }.Select(offset => new DailyTokens { Date = today.AddDays(offset), Tokens = offset == -6 ? (long?)null : offset == 0 ? 0 : 50 }).ToList() };
            Check(ranges.InRange(7, today).Count == 2 && ranges.InRange(30, today).Count == 4 && ranges.InRange(0, today).Count == 5, "History filters include exactly 7/30 UTC dates or all reported days");
            Check(ranges.InRange(7, today).Any(d => d.Tokens == null) && ranges.InRange(7, today).Any(d => d.Tokens == 0) && new ServiceUsage().InRange(30, today) == null, "Chart ranges retain missing and zero counts without inventing days");
            var account = new Account { Label = "Personal" };
            Check(Usage.Alerts(account, Sample(3, now + 100, now)).Single().Contains("5% warning"), "Initial urgent reading sends only the 5% alert");
            var store = new StateStore(scratch);
            var settings = new Settings(); settings.Accounts.Add(account); store.Save(settings);
            account = store.Load().Accounts.Single();
            Check(Usage.Alerts(account, Sample(3, now + 100, now + 1)).Count == 0, "Alert deduplication survives restart");
            Check(Usage.Alerts(account, Sample(80, now + 500, now + 20)).Count == 0 && account.Alerts["codex/primary"].Sent == 3, "Future reset adjustment does not rearm alerts");
            Usage.Alerts(account, Sample(0, now + 500, now + 400));
            Check(Usage.Alerts(account, Sample(99, now + 1000, now + 501)).Single().Contains("quota reset"), "Reset notification requires fresh restored capacity and a new window");
            Check(Usage.Alerts(account, Sample(20, now + 1000, now + 502)).Single().Contains("20% warning"), "New window rearms the 20% warning");
            Check(Usage.Alerts(account, Sample(5, now + 1000, now + 503)).Single().Contains("5% warning"), "5% warning follows 20% warning once");
            Check(Usage.Alerts(account, Sample(5, now + 1000, now + 504)).Count == 0, "Repeated urgent samples are quiet");
            var independent = new Account();
            bool firstWarning = Usage.Alerts(independent, Sample(20, now + 1000, now)).Count == 1;
            var secondWindow = Sample(20, now + 2000, now); secondWindow.Quotas[0].Key = "codex/secondary";
            Check(firstWarning && Usage.Alerts(independent, secondWindow).Count == 1 && Usage.Alerts(independent, Sample(20, now + 1000, now)).Count == 0, "Alert ledgers are independent across accounts and windows");
            var unknownReset = new Account();
            var withoutReset = Sample(0, now + 1000, now); withoutReset.Quotas[0].ResetsAt = null;
            Usage.Alerts(unknownReset, withoutReset);
            withoutReset.Quotas[0].Remaining = 100; Usage.Alerts(unknownReset, withoutReset);
            withoutReset.Quotas[0].Remaining = 5;
            Check(Usage.Alerts(unknownReset, withoutReset).Count == 0, "Missing reset timestamps do not invent a new alert window");
            Check(store.Load().StartWithWindows, "First-run startup preference defaults on without touching Windows");
            var legacy = Json.Serializer().Deserialize<Settings>(@"{""Accounts"":[],""StartWithWindows"":false}");
            Check(legacy.DarkTheme && !legacy.StartWithWindows, "Existing settings gain dark mode without re-enabling Windows startup");
            settings.DarkTheme = false; store.Save(settings);
            Check(!store.Load().DarkTheme, "The light-theme choice persists across settings reload");
            settings.StartWithWindows = false; store.Save(settings);
            Check(!store.Load().StartWithWindows && File.Exists(Path.Combine(scratch, "settings.json.bak")), "Startup opt-out persists with an atomic settings backup");
            bool invalid = false; try { store.Profile("..\\outside"); } catch (ArgumentException) { invalid = true; }
            Check(invalid, "Profile traversal is rejected");
            File.WriteAllText(Path.Combine(scratch, "settings.json"), "broken");
            invalid = false; try { store.Load(); } catch (ArgumentException) { invalid = true; }
            Check(invalid && File.ReadAllText(Path.Combine(scratch, "settings.json")) == "broken", "Corrupt settings fail closed and are preserved");
            var other = new Account();
            string own = store.Profile(account.Id), sibling = store.Profile(other.Id);
            Directory.CreateDirectory(own); Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(own, "owned-test-file"), "test"); File.WriteAllText(Path.Combine(sibling, "keep-test-file"), "test");
            store.DeleteProfile(account.Id);
            Check(!Directory.Exists(own) && File.Exists(Path.Combine(sibling, "keep-test-file")), "Profile deletion stays inside the selected app-owned directory");
            string oldHistory = Path.Combine(scratch, "history"), log = Path.Combine(oldHistory, "2026-08-27.jsonl");
            Directory.CreateDirectory(oldHistory); File.WriteAllText(log, "old samples"); File.WriteAllText(log + ".tmp", "interrupted write");
            string unexpected = Path.Combine(oldHistory, "keep.txt"); File.WriteAllText(unexpected, "not a history log");
            invalid = false; try { store.DeleteLegacyHistory(); } catch (IOException) { invalid = true; }
            Check(invalid && File.Exists(log) && File.Exists(unexpected), "History cleanup rejects unexpected files before deleting any logs");
            File.Delete(unexpected); Directory.CreateDirectory(unexpected);
            invalid = false; try { store.DeleteLegacyHistory(); } catch (IOException) { invalid = true; }
            Check(invalid && File.Exists(log) && Directory.Exists(unexpected), "History cleanup never recurses into unexpected directories");
            Directory.Delete(unexpected);
            settings.Accounts[0].Last = Sample(70, now + 100, now); store.Save(settings);
            string saved = File.ReadAllText(Path.Combine(scratch, "settings.json"));
            store.DeleteLegacyHistory(); store.DeleteLegacyHistory();
            Check(!Directory.Exists(oldHistory) && saved == File.ReadAllText(Path.Combine(scratch, "settings.json")) && File.Exists(Path.Combine(sibling, "keep-test-file")), "History cleanup is idempotent and preserves settings, snapshots, alerts, and profiles");
            Check(CodexClient.Identity(Json.Read(@"{""email"":""Example@invalid.test""}")) == CodexClient.Identity(Json.Read(@"{""email"":""example@invalid.test""}")), "Identity comparison does not expose account email");
        }
        private static async Task<string> AccountType(CodexClient client)
        {
            return Json.Text(Json.Map(Json.Get(await client.Request("account/read", new { refreshToken = false }), "account")), "type");
        }
        private static async Task Probe(string executable, string root)
        {
            var profiles = new StateStore(Path.Combine(root, "isolated-probe"));
            string a = profiles.Profile(Guid.NewGuid().ToString("N"));
            string b = profiles.Profile(Guid.NewGuid().ToString("N"));
            bool wroteA = false, wroteB = false;
            try
            {
                using (var first = await CodexClient.Start(executable, a))
                using (var second = await CodexClient.Start(executable, b))
                {
                    Check(await AccountType(first) == "" && await AccountType(second) == "", "Empty profiles do not inherit an existing Codex login");
                    wroteA = true;
                    await first.Request("account/login/start", new { type = "apiKey", apiKey = "sk-monitor-test-not-valid-" + Guid.NewGuid().ToString("N") });
                }
                using (var second = await CodexClient.Start(executable, b))
                {
                    Check(await AccountType(second) == "", "Profile B does not inherit profile A's credential after restart");
                    wroteB = true;
                    await second.Request("account/login/start", new { type = "apiKey", apiKey = "sk-monitor-test-not-valid-" + Guid.NewGuid().ToString("N") });
                }
                using (var first = await CodexClient.Start(executable, a))
                {
                    Check(await AccountType(first) == "apiKey", "Profile A credential survives process restart");
                    await first.Request("account/logout", new { }); wroteA = false;
                    Check(await AccountType(first) == "", "Logout clears only the selected profile");
                }
                using (var second = await CodexClient.Start(executable, b))
                    Check(await AccountType(second) == "apiKey", "Removing A leaves B signed in after restart");
                Check(!File.Exists(Path.Combine(a, "auth.json")) && !File.Exists(Path.Combine(b, "auth.json")), "Strict keyring storage creates no plaintext auth.json");
            }
            finally
            {
                // These homes are generated by this check; never log out the user's default home.
                if (wroteA) using (var first = CodexClient.Start(executable, a).GetAwaiter().GetResult()) first.Request("account/logout", new { }).GetAwaiter().GetResult();
                if (wroteB) using (var second = CodexClient.Start(executable, b).GetAwaiter().GetResult()) second.Request("account/logout", new { }).GetAwaiter().GetResult();
            }
            using (var second = await CodexClient.Start(executable, b))
                Check(await AccountType(second) == "", "Probe credentials cleaned up");
        }
    }
}
