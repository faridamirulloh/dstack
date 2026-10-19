using System.Collections.ObjectModel;
using MiniCodex.Presentation;

namespace MiniCodex.Codex;

public sealed class TranscriptEntry(ChatMessage first) : ObservableObject
{
    public string Key => first.Id;
    public ChatMessage Message => first;
    public string Role => first.Role;
    public bool IsActivity => Role is "Command" or "Files" or "Search";
    public ObservableCollection<ChatMessage> Items { get; } = [];
    public bool Expanded { get => first.TerminalExpanded; set { first.TerminalExpanded = value; Changed(); } }
    public string Heading
    {
        get
        {
            var terminals = Items.Count(item => item.Role == "Command"); var files = Items.Count(item => item.Role == "Files");
            var searches = Items.Count(item => item.Role == "Search");
            return string.Join(" · ", new[] { terminals > 0 ? $"{terminals} terminal{(terminals == 1 ? "" : "s")}" : "", files > 0 ? $"{files} file change{(files == 1 ? "" : "s")}" : "", searches > 0 ? $"{searches} search{(searches == 1 ? "" : "es")}" : "" }.Where(value => value.Length > 0));
        }
    }
    public string Preview => first.Preview;
    public string Text => string.Join("\n\n", Items.Select(item => item.CopyText));
    public void Update(IReadOnlyList<ChatMessage> items)
    {
        if (!Items.SequenceEqual(items))
        {
            for (var index = Items.Count - 1; index >= 0; index--) if (!items.Contains(Items[index])) Items.RemoveAt(index);
            for (var index = 0; index < items.Count; index++)
                if (index >= Items.Count || !ReferenceEquals(Items[index], items[index])) { var old = Items.IndexOf(items[index]); if (old >= 0) Items.Move(old, index); else Items.Insert(index, items[index]); }
        }
        Changed(nameof(Heading), nameof(Preview), nameof(Text));
    }
}
