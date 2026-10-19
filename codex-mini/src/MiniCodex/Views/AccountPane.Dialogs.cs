using System.Windows;
using MiniCodex.Presentation;

namespace MiniCodex.Views;

public partial class AccountPane
{
    private FrameworkElement? DialogAnchor => HostWindow.Panes.Count > 1 ? this : null;

    private T PrepareDialog<T>(T dialog) where T : Window
    {
        dialog.Owner = HostWindow;
        DialogPlacement.CenterOn(dialog, DialogAnchor);
        return dialog;
    }

    private MessageBoxResult ShowMessage(string message, string title = "Mini Codex", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        AppDialog.Show(HostWindow, message, title, buttons, icon, defaultResult, DialogAnchor);
}
