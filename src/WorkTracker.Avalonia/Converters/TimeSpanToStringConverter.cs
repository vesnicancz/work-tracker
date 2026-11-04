using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace WorkTracker.Avalonia.Converters;

/// <summary>
/// Converts <see cref="TimeSpan"/> values in two modes:
/// <list type="bullet">
///   <item>
///     Display mode (no parameter): formats as human-readable "2h 30m" summary used in the
///     DataGrid duration column. ConvertBack is not supported in this mode.
///   </item>
///   <item>
///     Edit mode (parameter = "HH:mm"): formats and parses a 24-hour "HH:mm" string for use in
///     edit-dialog TextBoxes. ConvertBack parses "HH:mm" back to a <see cref="TimeSpan"/>.
///   </item>
/// </list>
/// </summary>
public class TimeSpanToStringConverter : IValueConverter
{
	private static readonly string[] TimeOfDayFormats = [@"h\:mm", @"h\:m", @"h\:mm\:ss"];

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		// Edit mode: format as HH:mm
		if (parameter as string == "HH:mm")
		{
			if (value is TimeSpan ts)
			{
				return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}";
			}
			return string.Empty;
		}

		// Display mode: human-readable summary
		if (value is not TimeSpan timeSpan)
		{
			return "--";
		}

		if (timeSpan.TotalSeconds < 1)
		{
			return "0m";
		}

		var hours = (int)timeSpan.TotalHours;
		var minutes = timeSpan.Minutes;
		var seconds = timeSpan.Seconds;

		if (hours > 0)
		{
			return minutes > 0
				? $"{hours}h {minutes}m"
				: $"{hours}h";
		}

		if (minutes > 0)
		{
			return $"{minutes}m";
		}

		return $"{seconds}s";
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		// Edit mode round-trip: parse "HH:mm" back to TimeSpan
		if (parameter as string == "HH:mm" && value is string str)
		{
			// Not TimeSpan.TryParse: it reads a bare number as days, and the binding converts on every
			// keystroke, so typing "12:30" would pass through 12 days.
			if (TimeSpan.TryParseExact(str, TimeOfDayFormats, culture, out var ts))
			{
				return ts;
			}
		}

		return BindingOperations.DoNothing;
	}
}
