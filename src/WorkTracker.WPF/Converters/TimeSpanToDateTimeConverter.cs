using System.Globalization;
using System.Windows.Data;

namespace WorkTracker.WPF.Converters;

/// <summary>
/// Converts TimeSpan to DateTime for TimePicker binding (and vice versa)
/// </summary>
[ValueConversion(typeof(TimeSpan), typeof(DateTime?))]
[ValueConversion(typeof(TimeSpan?), typeof(DateTime?))]
public class TimeSpanToDateTimeConverter : IValueConverter
{
	private static readonly DateTime BaseDate = new DateTime(2000, 1, 1);

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value == null)
		{
			return null;
		}

		if (value is TimeSpan timeSpan)
		{
			return BaseDate.Add(timeSpan);
		}

		return null;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is DateTime dateTime)
		{
			// Round to minutes (remove seconds and milliseconds)
			var timeOfDay = dateTime.TimeOfDay;
			return new TimeSpan(timeOfDay.Hours, timeOfDay.Minutes, 0);
		}

		return targetType == typeof(TimeSpan?) ? null : TimeSpan.Zero;
	}
}