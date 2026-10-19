using System.Windows;
using MiniCodex.Presentation;

namespace MiniCodex.Views;

public partial class TextPromptWindow : Window
{
    public string Value => Input.Text.Trim();
    public TextPromptWindow(string title, string label, string initial)
    {
        InitializeComponent(); Title = title; Heading.Text = title; FieldLabel.Text = label; Input.Text = initial;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0) { Error.Text = "Enter a name."; Input.Focus(); return; }
        DialogResult = true;
    }
}
