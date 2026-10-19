using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using MiniCodex.Models;

namespace MiniCodex.Services;

public sealed class ProfileDrafts
{
    public Dictionary<Guid, string> Text { get; } = [];
    public Dictionary<Guid, List<ComposerReference>> References { get; } = [];
    public Dictionary<Guid, ObservableCollection<ChatAttachment>> Attachments { get; } = [];
    public Dictionary<Guid, ObservableCollection<ChatAnnotation>> Annotations { get; } = [];
    public Dictionary<Guid, long> Versions { get; } = [];
    public Dictionary<Guid, ObservableCollection<QueuedMessage>> Queues { get; } = [];
}

public sealed class DraftStore
{
    private sealed record QuoteData(string Quote, string Comment);
    private sealed record Entry(Guid Profile, Guid Chat, string Text, ComposerReference[] References, AttachmentReference[] Attachments, QuoteData[] Annotations, QueuedMessage[] Queue);
    private static readonly JsonSerializerOptions Json = new() { IgnoreReadOnlyProperties = true };
    private readonly AppController _controller;
    private readonly Dictionary<Guid, ProfileDrafts> _profiles = [];
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly string _path;
    private long _version;
    private bool _dirty;
    public string Error { get; private set; } = "";
    public event Action<Guid, Guid?>? Changed;

    public DraftStore(AppController controller, string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCodex", "drafts.json");
        _controller = controller;
        if (File.Exists(_path))
        {
            if (new FileInfo(_path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Draft recovery file exceeds 32 MiB; it was preserved.");
            var entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(_path), Json) ?? throw new InvalidDataException("Draft recovery file is empty.");
            foreach (var entry in entries)
            {
                if (!Exists(entry.Profile, entry.Chat)) continue;
                if (entry.Text is null || entry.References is null || entry.Attachments is null || entry.Annotations is null || entry.Queue is null
                    || entry.Attachments.Length > 20 || entry.Annotations.Length > 8 || entry.Queue.Length > 50)
                    throw new InvalidDataException("Draft recovery data is invalid; it was preserved.");
                if (entry.Queue.Any(item => item is null || item.Text is null || item.References is null || item.Attachments is null || item.Attachments.Length > 20 || string.IsNullOrWhiteSpace(item.ModelId)))
                    throw new InvalidDataException("Recovered queue data is invalid; it was preserved.");
                var profile = For(entry.Profile);
                profile.Text[entry.Chat] = entry.Text;
                profile.References[entry.Chat] = entry.References.ToList();
                profile.Attachments[entry.Chat] = new(entry.Attachments.Select(file => file.Open()));
                profile.Annotations[entry.Chat] = new(entry.Annotations.Select(quote => new ChatAnnotation(quote.Quote, quote.Comment)));
                profile.Queues[entry.Chat] = new(entry.Queue);
                foreach (var quote in profile.Annotations[entry.Chat]) WatchAnnotation(entry.Profile, entry.Chat, quote);
                foreach (var item in entry.Queue) WatchQueueItem(item);
            }
        }
        _save.Tick += (_, _) => { try { Flush(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { Error = error.Message; _save.Stop(); Changed?.Invoke(Guid.Empty, null); } };
        controller.Changed += (_, _) => Prune();
    }
    public ProfileDrafts For(Guid profile)
    {
        if (!_profiles.TryGetValue(profile, out var drafts)) _profiles[profile] = drafts = new();
        return drafts;
    }
    private bool Exists(Guid profile, Guid chat) => _controller.State.Profiles.FirstOrDefault(item => item.Id == profile)?.Projects.Any(project => project.Chats.Any(item => item.Id == chat)) == true;
    public void Touch(Guid profile, Guid chat)
    {
        For(profile).Versions[chat] = ++_version;
        Dirty(); Changed?.Invoke(profile, chat);
    }
    public void WatchAnnotation(Guid profile, Guid chat, ChatAnnotation quote) => quote.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChatAnnotation.Comment)) Touch(profile, chat); };
    private void WatchQueueItem(QueuedMessage item) => item.PropertyChanged += (_, _) => Dirty();
    public ObservableCollection<QueuedMessage> Queue(Guid profile, Guid chat)
    {
        var drafts = For(profile);
        if (!drafts.Queues.TryGetValue(chat, out var queue)) drafts.Queues[chat] = queue = [];
        return queue;
    }
    public void QueueChanged(QueuedMessage? added = null) { if (added is not null) WatchQueueItem(added); Dirty(); }
    private void Dirty() { _dirty = true; _save.Stop(); _save.Start(); }
    private void Prune()
    {
        foreach (var (profile, drafts) in _profiles)
        {
            var chats = drafts.Text.Keys.Concat(drafts.References.Keys).Concat(drafts.Attachments.Keys).Concat(drafts.Annotations.Keys).Concat(drafts.Queues.Keys).Distinct().ToArray();
            foreach (var chat in chats.Where(chat => !Exists(profile, chat)))
            {
                drafts.Text.Remove(chat); drafts.References.Remove(chat); drafts.Attachments.Remove(chat); drafts.Annotations.Remove(chat); drafts.Queues.Remove(chat); drafts.Versions.Remove(chat); Dirty();
            }
        }
    }
    public void Flush()
    {
        _save.Stop();
        if (!_dirty) return;
        var entries = new List<Entry>();
        foreach (var (profile, drafts) in _profiles)
            foreach (var chat in drafts.Text.Keys.Concat(drafts.References.Keys).Concat(drafts.Attachments.Keys).Concat(drafts.Annotations.Keys).Concat(drafts.Queues.Keys).Distinct())
            {
                if (!Exists(profile, chat)) continue;
                var body = drafts.Text.GetValueOrDefault(chat) ?? "";
                var refs = drafts.References.GetValueOrDefault(chat)?.ToArray() ?? [];
                var files = drafts.Attachments.GetValueOrDefault(chat)?.Select(file => new AttachmentReference(file.Path, file.IsImage)).ToArray() ?? [];
                var quotes = drafts.Annotations.GetValueOrDefault(chat)?.Select(quote => new QuoteData(quote.Quote, quote.Comment)).ToArray() ?? [];
                var queue = drafts.Queues.GetValueOrDefault(chat)?.ToArray() ?? [];
                if (body.Length > 0 || files.Length > 0 || quotes.Length > 0 || queue.Length > 0) entries.Add(new(profile, chat, body, refs, files, quotes, queue));
            }
        var content = JsonSerializer.SerializeToUtf8Bytes(entries, Json);
        if (content.Length > 32 * 1024 * 1024) throw new IOException("Drafts exceed the 32 MiB recovery limit. Remove unused drafts or queued messages.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(content); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak"); else File.Move(temporary, _path);
            _dirty = false; Error = "";
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
