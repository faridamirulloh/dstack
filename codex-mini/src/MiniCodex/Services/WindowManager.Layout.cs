using System.Windows;
using MiniCodex.Presentation;
using MiniCodex.Views;

namespace MiniCodex.Services;

public sealed partial class WindowManager
{
    private bool _restoringAccountLayout;

    public async Task RestoreAccountLayoutAsync()
    {
        var profiles = controller.State.Profiles.Select(profile => profile.Id).ToHashSet();
        var layouts = controller.State.AccountWindowLayouts
            .Select(layout => layout.Where(profiles.Contains).ToArray()).Where(layout => layout.Length > 0).ToArray();
        if (layouts.Length == 0)
        {
            if (controller.State.Profiles.FirstOrDefault() is { } profile) await OpenAccountAsync(profile.Id);
            else ShowAccounts();
            return;
        }
        _restoringAccountLayout = true;
        try
        {
            foreach (var layout in layouts)
            {
                AccountWindow? window = null;
                foreach (var profileId in layout)
                {
                    var pane = await OpenAccountAsync(profileId, newWindow: true, targetWindow: window);
                    window = pane.HostWindow;
                }
            }
        }
        finally { _restoringAccountLayout = false; }
        RememberAccountLayout();
    }

    internal bool RememberAccountLayout()
    {
        if (_restoringAccountLayout || IsExiting) return true;
        var layouts = _accountWindows.Where(window => window.Panes.Count > 0)
            .Select(window => window.Panes.Select(pane => pane.ProfileId).ToList()).ToList();
        var saved = controller.State.AccountWindowLayouts;
        // Keep the last layout when its final window has finished closing.
        if (layouts.Count == 0 || layouts.Count == saved.Count && layouts.Zip(saved).All(pair => pair.First.SequenceEqual(pair.Second))) return true;
        try
        {
            controller.Commit(state => state.AccountWindowLayouts = layouts);
            return true;
        }
        catch (Exception error)
        {
            AppDialog.Show(error.Message, "Could not save account layout", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
