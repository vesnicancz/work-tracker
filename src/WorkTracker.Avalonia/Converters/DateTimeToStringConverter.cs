using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WorkTracker.Avalonia.Converters;

/// <summary>
/// Converts DateTime to a formatted string. An optional format string can be passed as the
/// converter parameter (defaults to "HH:mm").
/// </summary>
public class DateTimeToStringConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is not DateTime dateTime)
		{
			return "--";
		}

		var format = parameter as string ?? "HH:mm";
		return dateTime.ToString(format);
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is string str && DateTime.TryParse(str, out var result))
		{
			return result;
		}

		return null;
	}
}
