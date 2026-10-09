using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace UsageLimitsMonitor
{
    internal static class Json
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 64 }; }
        public static string Write(object value) { return Serializer().Serialize(value); }
        public static Dictionary<string, object> Read(string text) { return Serializer().Deserialize<Dictionary<string, object>>(text); }
        public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(Dictionary<string, object> map, string key) { object value; return map.TryGetValue(key, out value) ? value : null; }
        public static string Text(Dictionary<string, object> map, string key) { return Convert.ToString(Get(map, key), CultureInfo.InvariantCulture); }
        public static double? Number(object value)
        {
            if (value == null || value is bool) return null;
            double number;
            if (!double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number) || double.IsNaN(number) || double.IsInfinity(number)) return null;
            return number;
        }
        public static long Now { get { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; } }
        public static DateTime LocalTime(long seconds) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime(); }
        public static void AtomicWrite(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(Write(value));
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }

    public sealed class Quota
    {
        public string Key { get; set; }
        public string Bucket { get; set; }
        public string Window { get; set; }
        public double? Remaining { get; set; }
        public long? ResetsAt { get; set; }
        public string Credits { get; set; }
        public string LimitState { get; set; }
    }
    public sealed class Snapshot
    {
        public long At { get; set; }
        public List<Quota> Quotas { get; set; }
        public long? AvailableResets { get; set; }
        public Snapshot() { Quotas = new List<Quota>(); }
    }
    public sealed class AlertState
    {
        public long? Reset { get; set; }
        public double? Remaining { get; set; }
        public int Sent { get; set; }
    }
    public sealed class Account
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Plan { get; set; }
        public string Identity { get; set; }
        public Snapshot Last { get; set; }
        public long? ManualSubscriptionAt { get; set; }
        public string ManualSubscriptionKind { get; set; }
        public Dictionary<string, AlertState> Alerts { get; set; }
        public Account() { Id = Guid.NewGuid().ToString("N"); Label = "Account"; Alerts = new Dictionary<string, AlertState>(); }
        public override string ToString() { return Label; }
    }
    public sealed class Settings
    {
        public List<Account> Accounts { get; set; }
        public string CodexPath { get; set; }
        public string PinnedAccount { get; set; }
        public string PinnedQuota { get; set; }
        public bool StartWithWindows { get; set; }
        public bool DarkTheme { get; set; }
        public Settings() { Accounts = new List<Account>(); StartWithWindows = true; DarkTheme = true; }
    }
    internal sealed class StateStore
    {
        public readonly string Root;
        public StateStore(string root) { Root = Path.GetFullPath(root); }
        public Settings Load()
        {
            string path = Path.Combine(Root, "settings.json");
            if (!File.Exists(path)) return new Settings();
            var state = Json.Serializer().Deserialize<Settings>(File.ReadAllText(path));
            if (state == null || state.Accounts == null) throw new InvalidDataException("Settings are invalid; the original file has been preserved.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in state.Accounts)
            {
                Guid id;
                if (account == null || !Guid.TryParseExact(account.Id, "N", out id) || !ids.Add(account.Id) || string.IsNullOrWhiteSpace(account.Label) || account.Label.Length > 80 || account.Label.Any(char.IsControl))
                    throw new InvalidDataException("An account entry is invalid; settings have been preserved.");
                if (account.Alerts == null) account.Alerts = new Dictionary<string, AlertState>();
                if (account.Alerts.Values.Any(a => a == null)) throw new InvalidDataException("Saved alert state is invalid; settings have been preserved.");
                if (account.ManualSubscriptionAt.HasValue ? account.ManualSubscriptionAt <= 0 || account.ManualSubscriptionAt >= 253402300800 ||
                    (account.ManualSubscriptionKind != "Renews" && account.ManualSubscriptionKind != "Ends") : !string.IsNullOrEmpty(account.ManualSubscriptionKind))
                    throw new InvalidDataException("A saved subscription date is invalid; settings have been preserved.");
                if (account.Last != null && (account.Last.At <= 0 || account.Last.At >= 253402300800 || account.Last.AvailableResets < 0 || account.Last.Quotas == null || account.Last.Quotas.Any(q => q == null || string.IsNullOrEmpty(q.Key) || q.Remaining < 0 || q.Remaining > 100 || q.ResetsAt <= 0 || q.ResetsAt >= 253402300800)))
                    throw new InvalidDataException("A saved snapshot is invalid; settings have been preserved.");
            }
            return state;
        }
        public void Save(Settings settings) { Json.AtomicWrite(Path.Combine(Root, "settings.json"), settings); }
        public void DeleteLegacyHistory()
        {
            string path = Path.GetFullPath(Path.Combine(Root, "history"));
            if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("History is outside the app's storage.");
            if (!Directory.Exists(path)) return;
            for (var parent = new DirectoryInfo(path); parent != null; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to remove history through a redirected directory.");
            var files = Directory.GetFileSystemEntries(path);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".tmp", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 4);
                DateTime date;
                if ((File.GetAttributes(file) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                    !name.EndsWith(".jsonl", StringComparison.Ordinal) || !DateTime.TryParseExact(name.Substring(0, name.Length - 6), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    throw new IOException("Unexpected files in the old history folder; no history files were deleted.");
            }
            foreach (string file in files) File.Delete(file);
            Directory.Delete(path); // Never recurse into unexpected files or account profiles.
        }
        public string Profile(string id)
        {
            Guid parsed;
            if (!Guid.TryParseExact(id, "N", out parsed)) throw new ArgumentException("Invalid account identifier.");
            return Path.Combine(Root, "profiles", id);
        }
        public void DeleteProfile(string id)
        {
            string path = Path.GetFullPath(Profile(id));
            string parent = Path.GetFullPath(Path.Combine(Root, "profiles")) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new IOException("Profile is outside the app's storage.");
            if (!Directory.Exists(path)) return;
            if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(parent.TrimEnd(Path.DirectorySeparatorChar)) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to remove a profile through a redirected directory.");
            EnsureNoRedirects(path);
            Directory.Delete(path, true);
        }
        private static void EnsureNoRedirects(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to remove a profile containing directory links.");
            foreach (string child in Directory.GetDirectories(directory)) EnsureNoRedirects(child);
        }
    }

    internal static class Usage
    {
        public static Snapshot Parse(Dictionary<string, object> response, long now)
        {
            var snapshot = new Snapshot { At = now, AvailableResets = Count(Json.Get(Json.Map(Json.Get(response, "rateLimitResetCredits")), "availableCount")) };
            var buckets = Json.Map(Json.Get(response, "rateLimitsByLimitId"));
            if (buckets.Count == 0)
            {
                var single = Json.Map(Json.Get(response, "rateLimits"));
                if (single.Count != 0) buckets[Nonempty(Json.Text(single, "limitId"), "codex")] = single;
            }
            foreach (var entry in buckets)
            {
                var bucket = Json.Map(entry.Value);
                if (bucket.Count == 0) continue;
                string name = Nonempty(Json.Text(bucket, "limitName"), entry.Key);
                string credits = CreditText(Json.Map(Json.Get(bucket, "credits")));
                string limitState = Json.Text(bucket, "rateLimitReachedType");
                bool found = false;
                foreach (string slot in new[] { "primary", "secondary" })
                {
                    var window = Json.Map(Json.Get(bucket, slot));
                    if (window.Count == 0) continue;
                    found = true;
                    var used = Json.Number(Json.Get(window, "usedPercent"));
                    var minutes = Json.Number(Json.Get(window, "windowDurationMins"));
                    var reset = Json.Number(Json.Get(window, "resetsAt"));
                    snapshot.Quotas.Add(new Quota {
                        Key = entry.Key + "/" + slot, Bucket = name,
                        Window = WindowName(minutes, slot),
                        Remaining = used.HasValue && used.Value >= 0 ? (double?)Math.Max(0, 100 - used.Value) : null,
                        ResetsAt = reset.HasValue && reset > 0 && reset < 253402300800 && reset == Math.Truncate(reset.Value) ? (long?)reset.Value : null,
                        Credits = credits, LimitState = limitState
                    });
                }
                if (!found) snapshot.Quotas.Add(new Quota { Key = entry.Key + "/none", Bucket = name, Window = "Not reported", Credits = credits, LimitState = limitState });
            }
            return snapshot;
        }
        public static string Nonempty(string value, string fallback) { return string.IsNullOrWhiteSpace(value) ? fallback : value; }
        public static string CreditText(Dictionary<string, object> credits)
        {
            if (object.Equals(Json.Get(credits, "unlimited"), true)) return "Unlimited";
            var balance = Json.Number(Json.Get(credits, "balance"));
            if (balance.HasValue && balance.Value >= 0) return balance.Value.ToString("0.##", CultureInfo.InvariantCulture);
            if (object.Equals(Json.Get(credits, "hasCredits"), false)) return "No credits reported";
            return "Unavailable";
        }
        public static string WindowName(double? minutes, string fallback)
        {
            if (!minutes.HasValue || minutes <= 0) return fallback;
            if (minutes.Value % 1440 == 0) return (minutes.Value / 1440).ToString("0", CultureInfo.InvariantCulture) + " day";
            if (minutes.Value % 60 == 0) return (minutes.Value / 60).ToString("0", CultureInfo.InvariantCulture) + " hour";
            return minutes.Value.ToString("0.#", CultureInfo.InvariantCulture) + " min";
        }
        public static string ResetText(Quota quota, long now)
        {
            if (!quota.ResetsAt.HasValue) return "Not reported";
            if (quota.ResetsAt <= now) return "Awaiting confirmation";
            var time = TimeSpan.FromSeconds(quota.ResetsAt.Value - now);
            string remaining = time.TotalDays >= 1 ? ((int)time.TotalDays) + "d " + time.Hours + "h" : time.TotalHours >= 1 ? ((int)time.TotalHours) + "h " + time.Minutes + "m" : Math.Max(1, time.Minutes) + "m";
            return remaining + " (" + Json.LocalTime(quota.ResetsAt.Value).ToString("ddd HH:mm") + ")";
        }
        public static List<Quota> AccountQuotas(Account account)
        {
            return account.Last == null ? new List<Quota>() : account.Last.Quotas.OrderBy(q => q.Key, StringComparer.Ordinal).ToList();
        }
        private static string BucketKey(Quota quota)
        {
            int separator = quota.Key.LastIndexOf('/');
            return separator < 0 ? quota.Key : quota.Key.Substring(0, separator);
        }
        public static string CompactWindow(Quota quota)
        {
            return Nonempty(quota.Window, "Not reported").Replace(" day", "d").Replace(" hour", "h").Replace(" min", "m");
        }
        public static string QuotaValues(Account account, Func<Quota, string> format, string unavailable)
        {
            var quotas = AccountQuotas(account);
            return quotas.Count == 0 ? unavailable : string.Join(" | ", quotas.Select(format));
        }
        public static string WindowLabels(Account account)
        {
            bool multipleBuckets = AccountQuotas(account).Select(BucketKey).Distinct(StringComparer.Ordinal).Count() > 1;
            return QuotaValues(account, q => (multipleBuckets ? q.Bucket + " " : "") + CompactWindow(q), "Not reported");
        }
        public static string RemainingText(Quota quota)
        {
            return quota.Remaining.HasValue ? quota.Remaining.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "Unavailable";
        }
        public static string ResetCountdown(Quota quota, long now)
        {
            if (!quota.ResetsAt.HasValue) return "Not reported";
            var remaining = TimeSpan.FromSeconds(quota.ResetsAt.Value - now);
            return remaining.TotalSeconds <= 0 ? "Awaiting confirmation" : remaining.TotalDays >= 1 ? ((int)remaining.TotalDays) + "d" :
                remaining.TotalHours >= 1 ? ((int)remaining.TotalHours) + "h" : Math.Max(1, (int)remaining.TotalMinutes) + "m";
        }
        public static string ResetDate(Quota quota)
        {
            return quota.ResetsAt.HasValue ? Json.LocalTime(quota.ResetsAt.Value).ToString("d-MMM HH:mm", CultureInfo.InvariantCulture) : "Not reported";
        }
        public static string AccountCredits(Account account)
        {
            var buckets = AccountQuotas(account).GroupBy(BucketKey).ToList();
            return buckets.Count == 0 ? "Unavailable" : string.Join(" | ", buckets.Select(g =>
                (buckets.Count > 1 ? g.First().Bucket + ": " : "") + Nonempty(g.First().Credits, "Unavailable")));
        }
        public static string AvailableResetsText(Account account)
        {
            return account.Last != null && account.Last.AvailableResets.HasValue ? account.Last.AvailableResets.Value.ToString(CultureInfo.InvariantCulture) : "Unavailable";
        }
        public static bool IsFree(Account account) { return string.Equals(account.Plan, "free", StringComparison.OrdinalIgnoreCase); }
        public static string SubscriptionText(Account account, long now)
        {
            if (IsFree(account)) return "Not applicable";
            if (!account.ManualSubscriptionAt.HasValue) return "Unavailable";
            return account.ManualSubscriptionKind + " " + Json.LocalTime(account.ManualSubscriptionAt.Value).ToString("d-MMM-yyyy HH:mm", CultureInfo.InvariantCulture) +
                " (Manual" + (account.ManualSubscriptionAt <= now ? "; past" : "") + ")";
        }
        public static string AccountTip(Account account, bool fresh, long now)
        {
            return account.Label + ": " + WindowLabels(account) + " / " + QuotaValues(account, RemainingText, "Unavailable") + " / " +
                QuotaValues(account, q => ResetCountdown(q, now), "Not reported") + " / " + QuotaValues(account, ResetDate, "Not reported") +
                " / Resets: " + AvailableResetsText(account) + (fresh || account.Last == null ? "" : " (stale)") +
                " / Subscription: " + SubscriptionText(account, now);
        }
        public static ServiceUsage ParseHistory(Dictionary<string, object> response)
        {
            var summary = Json.Map(Json.Get(response, "summary"));
            var usage = new ServiceUsage { LifetimeTokens = Count(Json.Get(summary, "lifetimeTokens")), PeakDailyTokens = Count(Json.Get(summary, "peakDailyTokens")) };
            var days = Json.Get(response, "dailyUsageBuckets") as IList;
            if (days != null)
            {
                usage.Days = new List<DailyTokens>();
                foreach (object item in days)
                {
                    var day = Json.Map(item); DateTime date;
                    if (!DateTime.TryParseExact(Json.Text(day, "startDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                        throw new InvalidDataException("OpenAI returned an invalid daily usage date. History has not been replaced with estimated data.");
                    usage.Days.Add(new DailyTokens { Date = date, Tokens = Count(Json.Get(day, "tokens")) });
                }
                if (usage.Days.GroupBy(d => d.Date).Any(g => g.Count() > 1)) throw new InvalidDataException("OpenAI returned duplicate daily usage dates. History totals are unavailable.");
                usage.Days = usage.Days.OrderByDescending(d => d.Date).ToList();
            }
            return usage;
        }
        private static long? Count(object value)
        {
            long count;
            return value != null && !(value is bool) && long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count >= 0 ? (long?)count : null;
        }
        public static List<string> Alerts(Account account, Snapshot next)
        {
            var messages = new List<string>();
            foreach (var quota in next.Quotas)
            {
                if (!quota.Remaining.HasValue) continue;
                AlertState previous;
                if (!account.Alerts.TryGetValue(quota.Key, out previous)) previous = new AlertState();
                bool newWindow = previous.Reset.HasValue && quota.ResetsAt.HasValue && previous.Reset <= next.At && quota.ResetsAt > previous.Reset;
                if (newWindow)
                {
                    if (previous.Remaining.HasValue && previous.Remaining <= 0 && quota.Remaining > 0)
                        messages.Add(quota.Bucket + " / " + quota.Window + ": quota reset; " + quota.Remaining.Value.ToString("0.#") + "% available.");
                    previous.Sent = 0;
                }
                if (quota.Remaining <= 5 && (previous.Sent & 2) == 0)
                {
                    messages.Add(quota.Bucket + " / " + quota.Window + ": " + quota.Remaining.Value.ToString("0.#") + "% remaining (5% warning).");
                    previous.Sent |= 3;
                }
                else if (quota.Remaining <= 20 && (previous.Sent & 1) == 0)
                {
                    messages.Add(quota.Bucket + " / " + quota.Window + ": " + quota.Remaining.Value.ToString("0.#") + "% remaining (20% warning).");
                    previous.Sent |= 1;
                }
                previous.Reset = quota.ResetsAt ?? previous.Reset;
                previous.Remaining = quota.Remaining;
                account.Alerts[quota.Key] = previous;
            }
            return messages;
        }
    }

    internal sealed class DailyTokens
    {
        public DateTime Date;
        public long? Tokens;
    }
    internal sealed class ServiceUsage
    {
        public long? LifetimeTokens;
        public long? PeakDailyTokens;
        public List<DailyTokens> Days;
        public List<DailyTokens> InRange(int days, DateTime today)
        {
            if (Days == null) return null;
            var first = days == 0 ? DateTime.MinValue : today.Date.AddDays(1 - days);
            return Days.Where(d => days == 0 || (d.Date >= first && d.Date <= today.Date)).ToList();
        }
    }
}
