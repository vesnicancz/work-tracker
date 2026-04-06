using System.Globalization;
using FluentAssertions;
using WorkTracker.Avalonia.Converters;

namespace WorkTracker.Avalonia.Tests.Converters;

public class BoolToOpacityConverterTests
{
	[Fact]
	public void Convert_True_ReturnsFullOpacity()
	{
		var result = BoolToOpacityConverter.Instance.Convert(true, typeof(double), null, CultureInfo.InvariantCulture);

		result.Should().Be(1.0);
	}

	[Fact]
	public void Convert_False_ReturnsHalfOpacity()
	{
		var result = BoolToOpacityConverter.Instance.Convert(false, typeof(double), null, CultureInfo.InvariantCulture);

		result.Should().Be(0.5);
	}

	[Fact]
	public void Convert_Null_ReturnsHalfOpacity()
	{
		var result = BoolToOpacityConverter.Instance.Convert(null, typeof(double), null, CultureInfo.InvariantCulture);

		result.Should().Be(0.5);
	}

	[Fact]
	public void Instance_ReturnsSameInstance()
	{
		var a = BoolToOpacityConverter.Instance;
		var b = BoolToOpacityConverter.Instance;

		a.Should().BeSameAs(b);
	}

	[Fact]
	public void ConvertBack_ThrowsNotImplementedException()
	{
		var act = () => BoolToOpacityConverter.Instance.ConvertBack(1.0, typeof(bool), null, CultureInfo.InvariantCulture);

		act.Should().Throw<NotImplementedException>();
	}
}
