using System.Globalization;
using FluentAssertions;
using WorkTracker.Avalonia.Converters;

namespace WorkTracker.Avalonia.Tests.Converters;

public class TimeSpanToStringConverterTests
{
	private readonly TimeSpanToStringConverter _converter = new();

	[Fact]
	public void Convert_Display_HoursAndMinutes()
	{
		var result = _converter.Convert(new TimeSpan(2, 30, 0), typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("2h 30m");
	}

	[Fact]
	public void Convert_Display_HoursOnly()
	{
		var result = _converter.Convert(new TimeSpan(3, 0, 0), typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("3h");
	}

	[Fact]
	public void Convert_Display_MinutesOnly()
	{
		var result = _converter.Convert(new TimeSpan(0, 45, 0), typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("45m");
	}

	[Fact]
	public void Convert_Display_SecondsOnly()
	{
		var result = _converter.Convert(new TimeSpan(0, 0, 15), typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("15s");
	}

	[Fact]
	public void Convert_Display_LessThanOneSecond_ReturnsZeroMinutes()
	{
		var result = _converter.Convert(TimeSpan.FromMilliseconds(500), typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("0m");
	}

	[Fact]
	public void Convert_Display_Null_ReturnsDashes()
	{
		var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("--");
	}

	[Fact]
	public void Convert_EditMode_FormatsAsHHmm()
	{
		var result = _converter.Convert(new TimeSpan(9, 5, 0), typeof(string), "HH:mm", CultureInfo.InvariantCulture);

		result.Should().Be("09:05");
	}

	[Fact]
	public void Convert_EditMode_Null_ReturnsEmpty()
	{
		var result = _converter.Convert(null, typeof(string), "HH:mm", CultureInfo.InvariantCulture);

		result.Should().Be(string.Empty);
	}

	[Fact]
	public void ConvertBack_EditMode_ParsesHHmm()
	{
		var result = _converter.ConvertBack("9:05", typeof(TimeSpan), "HH:mm", CultureInfo.InvariantCulture);

		result.Should().Be(new TimeSpan(9, 5, 0));
	}

	[Fact]
	public void Convert_Display_LargeHours_FormatsCorrectly()
	{
		var result = _converter.Convert(new TimeSpan(25, 30, 0), typeof(string), null, CultureInfo.InvariantCulture);

		result.Should().Be("25h 30m");
	}
}
