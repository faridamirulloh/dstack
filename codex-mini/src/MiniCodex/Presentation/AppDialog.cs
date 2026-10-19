using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MiniCodex.Presentation;

public static class AppDialog
{
    public static MessageBoxResult Show(string message, string title = "Mini Codex", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None, FrameworkElement? placementTarget = null) => ShowCore(null, message, title, buttons, icon, defaultResult, placementTarget);

    public static MessageBoxResult Show(Window owner, string message, string title = "Mini Codex", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None, FrameworkElement? placementTarget = null) => ShowCore(owner, message, title, buttons, icon, defaultResult, placementTarget);

    private static MessageBoxResult ShowCore(Window? owner, string message, string title, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult, FrameworkElement? placementTarget = null)
    {
        owner ??= Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive && window.IsVisible);
        var choices = buttons switch
        {
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            _ => new[] { MessageBoxResult.OK }
        };
        var result = choices.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel : choices.Contains(MessageBoxResult.No) ? MessageBoxResult.No : MessageBoxResult.OK;
        if (!choices.Contains(defaultResult)) defaultResult = result;
        var dialog = new Window
        {
            Title = title, Width = 510, MinWidth = 360, MaxWidth = 650, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Style = Application.Current.TryFindResource(typeof(Window)) as Style,
            Background = new SolidColorBrush(Color.FromRgb(23, 24, 27)), Foreground = new SolidColorBrush(Color.FromRgb(233, 234, 237)),
            WindowStartupLocation = owner?.IsVisible == true ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        if (owner?.IsVisible == true) dialog.Owner = owner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap };
        if (icon is MessageBoxImage.Warning or MessageBoxImage.Error) heading.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        panel.Children.Add(heading);
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21 }
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        Button? focus = null;
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.ToString(), MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), IsDefault = choice == defaultResult, IsCancel = choice == result };
            if (choice == defaultResult) focus = button;
            button.Click += (_, _) => { result = choice; dialog.DialogResult = true; };
            actions.Children.Add(button);
        }
        panel.Children.Add(actions); dialog.Content = panel;
        dialog.SourceInitialized += (_, _) => DarkTitleBar.Apply(dialog);
        dialog.Loaded += (_, _) => focus?.Focus();
        DialogPlacement.CenterOn(dialog, placementTarget);
        dialog.ShowDialog();
        return result;
    }
}
