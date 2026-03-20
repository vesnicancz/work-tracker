using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WorkTracker.Avalonia.Converters;

/// <summary>
/// Returns <see langword="true"/> when the bound value is not null, <see langword="false"/> when
/// it is null. Use with <c>IsVisible</c> to replace the WPF NullToVisibilityConverter.
/// Pass "Inverse" as the converter parameter to reverse the logic.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNotNull = value is not null;
        var isInverse = parameter as string == "Inverse";
        return isInverse ? !isNotNull : isNotNull;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
