using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AgentView.TokenCounter.Converters;

/// <summary>
/// Converts a <c>UsedPct</c> integer to the foreground brush for the
/// big percentage number.
/// </summary>
/// <remarks>
/// Returns <c>Brush.TextMuted</c> at 0% (signals "nothing posted yet"
/// without yelling), and <c>Brush.TextPrimary</c> for any non-zero
/// value. Severity is deliberately NOT expressed through the number
/// colour — see <see cref="SeverityBrushConverter"/> for the
/// rationale.
/// </remarks>
[ValueConversion(typeof(int), typeof(Brush))]
public sealed class NumberInkBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var pct = value is int i ? i : 0;
        var key = pct == 0 ? "Brush.TextMuted" : "Brush.TextPrimary";
        return (Brush)System.Windows.Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
