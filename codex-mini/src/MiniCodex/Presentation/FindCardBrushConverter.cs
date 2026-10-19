using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MiniCodex.Models;

namespace MiniCodex.Presentation;

public sealed class FindCardBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var text = values.ElementAtOrDefault(0) switch
        {
            ChatAnnotation quote => quote.Quote + "\n" + quote.Comment,
            QuestionReply question => question.Header + "\n" + question.Question + "\n" + (question.HasAnswer ? question.Answer : string.Join("\n", question.Options.Select(option => option.Label + "\n" + option.Description))),
            _ => ""
        };
        var query = values.ElementAtOrDefault(1) as string;
        return Application.Current.FindResource(!string.IsNullOrEmpty(query) && text.Contains(query, StringComparison.OrdinalIgnoreCase) ? "AccentBrush" : "LineBrush");
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
