using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace MiniCodex.Services;

public static class UsageResetPage
{
    public static Uri Url { get; } = new("https://chatgpt.com/codex/settings/usage");

    public static string BrowserFolder(Guid profileId, string codexHome, string? storageRoot = null)
    {
        var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(codexHome)).ToUpperInvariant();
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(home)))[..16];
        return Path.Combine(storageRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCodex", "browser"), profileId.ToString("N"), scope);
    }

    public static bool TryWebUrl(string? value, out Uri? url)
    {
        url = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) || candidate.Scheme != Uri.UriSchemeHttps || candidate.UserInfo.Length > 0 || candidate.Host.Length == 0) return false;
        url = candidate; return true;
    }

    public static bool CanNavigate(string? value) => value == "about:blank" || TryWebUrl(value, out _);

    public static void OpenExternally(Uri url)
    {
        if (!TryWebUrl(url.AbsoluteUri, out _)) throw new ArgumentException("Only HTTPS pages can be opened.");
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
}
