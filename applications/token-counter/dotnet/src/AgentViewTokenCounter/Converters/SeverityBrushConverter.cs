using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AgentView.TokenCounter.Converters;

/// <summary>
/// Converts a <c>UsedPct</c> integer (0..100) to the four-tier
/// severity brush used for the progress-bar fill.
/// </summary>
/// <remarks>
/// <para>
/// The number itself always stays in primary ink (<c>Brush.TextPrimary</c>
/// or <c>Brush.TextMuted</c> at 0%). Severity is communicated only
/// through the bar — this is the "confident neutral type" move from
/// Linear / Stripe where colour is reserved for the gauge, not the label.
/// </para>
/// <para>
/// Four-tier scale that mirrors <c>display.html</c>'s <c>severityFor()</c>:
/// <list type="bullet">
///   <item>&lt;30% — calm green  (<c>Brush.Bar.Low</c>)</item>
///   <item>&lt;80% — amber       (<c>Brush.Bar.Mid</c>)</item>
///   <item>&lt;95% — orange      (<c>Brush.Bar.High</c>)</item>
///   <item>≥95%  — red           (<c>Brush.Bar.Crit</c>)</item>
/// </list>
/// 0% renders as the divider colour so an empty bar doesn't look
/// like a deliberate colour choice.
/// </para>
/// </remarks>
[ValueConversion(typeof(int), typeof(Brush))]
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var pct = value is int i ? i : 0;

        // All keys are defined in Themes/Tokens.xaml and surfaced as
        // SolidColorBrush resources. FindResource would need a
        // FrameworkElement reference; instead we keep a reference to
        // the application-level resource dictionary which is always
        // available once App.OnStartup has run.
        var key = pct switch
        {
            0        => "Brush.Divider",
            >= 95    => "Brush.Bar.Crit",
            >= 80    => "Brush.Bar.High",
            >= 30    => "Brush.Bar.Mid",
            _        => "Brush.Bar.Low",
        };
        return (Brush)System.Windows.Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
