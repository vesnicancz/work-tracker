using System.Globalization;
using System.Windows.Data;

namespace WorkTracker.WPF.Converters;

/// <summary>
/// Converts TimeSpan to human-readable string format (e.g., "2h 30m")
/// </summary>
[ValueConversion(typeof(TimeSpan?), typeof(string))]
public class TimeSpanToStringConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
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

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}