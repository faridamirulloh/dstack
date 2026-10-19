namespace MiniCodex.Codex;

public sealed record AccountAuthentication(bool SignedIn, string Identity, DateTimeOffset CheckedAt);
