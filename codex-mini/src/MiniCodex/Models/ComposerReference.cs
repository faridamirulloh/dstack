namespace MiniCodex.Models;

public enum ReferenceKind { Skill, Plugin, Connector, Folder, File }

public sealed record ComposerReference(ReferenceKind Kind, string Name, string Path, string Description = "", bool Enabled = true, string Source = "", string? PluginId = null)
{
    public string KindLabel => Kind.ToString();
    public string Detail => string.Join(" · ", new[] { KindLabel, Enabled ? Source : "Unavailable", Description }.Where(value => value.Length > 0));
    public string Tooltip => Detail + "\n" + Path;
    public string Token
    {
        get
        {
            var label = Name.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]").ReplaceLineEndings(" ");
            var prefix = Kind == ReferenceKind.Skill ? "/" : Kind == ReferenceKind.Connector ? "$" : "@";
            var target = Kind is ReferenceKind.File or ReferenceKind.Folder ? new Uri(Path).AbsoluteUri : Path;
            target = target.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29").Replace("<", "%3C").Replace(">", "%3E");
            return $"[{prefix}{label}]({target})";
        }
    }
    public bool IsInText(string text)
    {
        var token = Token;
        return text.Contains(token, StringComparison.Ordinal)
            || Kind == ReferenceKind.Skill && text.Contains("[$" + token[2..], StringComparison.Ordinal);
    }
}

public sealed record CapabilitySnapshot(IReadOnlyList<ComposerReference> Entries, string Notice);
