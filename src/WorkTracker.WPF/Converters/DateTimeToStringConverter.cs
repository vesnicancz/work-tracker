using System.Globalization;
using System.Windows.Data;

namespace WorkTracker.WPF.Converters;

/// <summary>
/// Converts DateTime to formatted string with optional format parameter
/// </summary>
[ValueConversion(typeof(DateTime?), typeof(string))]
public class DateTimeToStringConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is not DateTime dateTime)
		{
			return "--";
		}

		var format = parameter as string ?? "HH:mm";
		return dateTime.ToString(format);
	}

	public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is string str && DateTime.TryParse(str, out var result))
		{
			return result;
		}

		return null;
	}
}