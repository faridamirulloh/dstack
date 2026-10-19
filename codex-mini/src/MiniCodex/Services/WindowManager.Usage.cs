using System.Windows;
using MiniCodex.Codex;
using MiniCodex.Presentation;
using MiniCodex.Views;

namespace MiniCodex.Services;

public sealed partial class WindowManager
{
    private readonly Dictionary<Guid, AccountBrowserWindow> _usageBrowsers = [];

    public void OpenUsageResets(Guid profileId, Window owner, FrameworkElement? placementTarget = null)
    {
        if (IsExiting || Runtimes.IsStopping || Runtimes.IsRemoving(profileId)) return;
        var profile = controller.Profile(profileId);
        if (_usageBrowsers.TryGetValue(profileId, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Owner = owner;
            existing.Show(); DialogPlacement.CenterOn(existing, placementTarget); existing.Activate(); return;
        }
        var identity = Runtimes.Authentication(profileId) is { SignedIn: true } authentication ? authentication.Identity : "";
        var browser = new AccountBrowserWindow(profile.Name, identity, UsageResetPage.Url, UsageResetPage.BrowserFolder(profileId, CodexInstallation.Home(profile))) { Owner = owner };
        DialogPlacement.CenterOn(browser, placementTarget);
        _usageBrowsers[profileId] = browser;
        browser.Closing += (_, _) => owner = browser.Owner ?? owner;
        browser.Closed += async (_, _) =>
        {
            _usageBrowsers.Remove(profileId);
            if (owner.IsVisible && owner is not AccountWindow { IsClosing: true }
                && (owner is not AccountWindow || HasAccountWindow(profileId))) await RefreshUsageAfterBrowserAsync(profileId);
        };
        browser.Show();
    }

    private void CloseUsageResets(Guid profileId)
    {
        if (_usageBrowsers.TryGetValue(profileId, out var browser)) browser.Close();
    }

    private async Task RefreshUsageAfterBrowserAsync(Guid profileId)
    {
        if (IsExiting || Runtimes.IsStopping || Runtimes.IsRemoving(profileId) || !controller.State.Profiles.Any(profile => profile.Id == profileId)) return;
        RuntimeRegistry.RuntimeLease? lease = null;
        try
        {
            lease = await Runtimes.BorrowAsync(profileId);
            if (!lease.Runtime.Connected) await lease.Runtime.ConnectAsync(false);
            if (lease.Runtime.SignedIn) await lease.Runtime.RefreshUsageAsync();
        }
        catch (Exception error) { Runtimes.Find(profileId)?.Report(error); }
        finally
        {
            if (lease is not null)
            {
                try
                {
                    try { if (!HasAccountWindow(profileId) && !lease.Runtime.HasWork) await Runtimes.ReleaseAsync(profileId); }
                    finally { await lease.DisposeAsync(); }
                }
                catch (Exception error) { Runtimes.Find(profileId)?.Report(error); }
            }
        }
    }
}
