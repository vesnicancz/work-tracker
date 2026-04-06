using System.Globalization;
using FluentAssertions;
using WorkTracker.Avalonia.Converters;

namespace WorkTracker.Avalonia.Tests.Converters;

public class NullToBoolConverterTests
{
	private readonly NullToBoolConverter _converter = new();

	[Fact]
	public void Convert_NonNull_ReturnsTrue()
	{
		var result = _converter.Convert("something", typeof(bool), null, CultureInfo.InvariantCulture);

		result.Should().Be(true);
	}

	[Fact]
	public void Convert_Null_ReturnsFalse()
	{
		var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);

		result.Should().Be(false);
	}

	[Fact]
	public void Convert_NonNull_Inverse_ReturnsFalse()
	{
		var result = _converter.Convert("something", typeof(bool), "Inverse", CultureInfo.InvariantCulture);

		result.Should().Be(false);
	}

	[Fact]
	public void Convert_Null_Inverse_ReturnsTrue()
	{
		var result = _converter.Convert(null, typeof(bool), "Inverse", CultureInfo.InvariantCulture);

		result.Should().Be(true);
	}

	[Fact]
	public void ConvertBack_ThrowsNotImplementedException()
	{
		var act = () => _converter.ConvertBack(true, typeof(object), null, CultureInfo.InvariantCulture);

		act.Should().Throw<NotImplementedException>();
	}
}
