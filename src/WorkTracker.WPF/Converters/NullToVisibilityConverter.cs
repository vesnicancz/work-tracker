using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkTracker.WPF.Converters;

/// <summary>
/// Converts null to Visibility (null = Collapsed, not null = Visible)
/// Use parameter "Inverse" to invert the logic
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var isNull = value == null;
		var isInverse = parameter as string == "Inverse";

		if (isInverse)
		{
			isNull = !isNull;
		}

		return isNull ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}