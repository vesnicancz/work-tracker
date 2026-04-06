using System.Globalization;
using FluentAssertions;
using WorkTracker.Avalonia.Converters;

namespace WorkTracker.Avalonia.Tests.Converters;

public class DateTimeToStringConverterTests
{
	private readonly DateTimeToStringConverter _converter = new();

	[Fact]
	public void Convert_DateTime_ReturnsDefaultFormat()
	{
		var dt = new DateTime(2026, 4, 6, 14, 30, 0);

		var result = _converter.Convert(dt, typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("14:30");
	}

	[Fact]
	public void Convert_DateTime_WithCustomFormat_UsesFormat()
	{
		var dt = new DateTime(2026, 4, 6, 14, 30, 0);

		var result = _converter.Convert(dt, typeof(string), "yyyy-MM-dd", CultureInfo.InvariantCulture);

		result.Should().Be("2026-04-06");
	}

	[Fact]
	public void Convert_Null_ReturnsDashes()
	{
		var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("--");
	}

	[Fact]
	public void Convert_NonDateTime_ReturnsDashes()
	{
		var result = _converter.Convert("not a date", typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("--");
	}

	[Fact]
	public void ConvertBack_ValidString_ReturnsDateTime()
	{
		var result = _converter.ConvertBack("2026-04-06", typeof(DateTime), null, CultureInfo.InvariantCulture);

		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	public void ConvertBack_InvalidString_ReturnsNull()
	{
		var result = _converter.ConvertBack("not a date", typeof(DateTime), null, CultureInfo.InvariantCulture);

		result.Should().BeNull();
	}

	[Fact]
	public void ConvertBack_Null_ReturnsNull()
	{
		var result = _converter.ConvertBack(null, typeof(DateTime), null, CultureInfo.InvariantCulture);

		result.Should().BeNull();
	}
}
