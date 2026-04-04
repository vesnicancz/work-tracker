using System.Globalization;
using Avalonia.Data.Converters;

namespace WorkTracker.Avalonia.Converters;

/// <summary>
/// Returns 1.0 when true, 0.5 when false. Used to visually dim deselected items.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
	public static readonly BoolToOpacityConverter Instance = new();

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is true ? 1.0 : 0.5;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
