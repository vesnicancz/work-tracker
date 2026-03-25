using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkTracker.WPF.Converters;

/// <summary>
/// Converts bool to Visibility (true = Visible, false = Collapsed)
/// Use parameter "Inverse" to invert the logic
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var isVisible = value is bool b && b;
		var isInverse = parameter as string == "Inverse";

		if (isInverse)
		{
			isVisible = !isVisible;
		}

		return isVisible ? Visibility.Visible : Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var isVisible = value is Visibility v && v == Visibility.Visible;
		var isInverse = parameter as string == "Inverse";

		return isInverse ? !isVisible : isVisible;
	}
}