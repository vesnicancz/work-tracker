using FluentAssertions;
using WorkTracker.UI.Shared.Helpers;

namespace WorkTracker.UI.Shared.Tests.Orchestrators;

public class DurationFormatterTests
{
	[Fact]
	public void Format_ZeroSeconds_ReturnsZeroMinutes()
	{
		DurationFormatter.Format(0).Should().Be("0m");
	}

	[Fact]
	public void Format_LessThanOneMinute_ReturnsZeroMinutes()
	{
		DurationFormatter.Format(59).Should().Be("0m");
	}

	[Fact]
	public void Format_ExactlyOneMinute_ReturnsOneMinute()
	{
		DurationFormatter.Format(60).Should().Be("1m");
	}

	[Fact]
	public void Format_MinutesOnly_ReturnsMinutes()
	{
		DurationFormatter.Format(45 * 60).Should().Be("45m");
	}

	[Fact]
	public void Format_ExactlyOneHour_ReturnsOneHour()
	{
		DurationFormatter.Format(3600).Should().Be("1h");
	}

	[Fact]
	public void Format_HoursAndMinutes_ReturnsCombined()
	{
		DurationFormatter.Format(2 * 3600 + 30 * 60).Should().Be("2h 30m");
	}

	[Fact]
	public void Format_LargeValue_ReturnsCorrectHours()
	{
		DurationFormatter.Format(25 * 3600 + 15 * 60).Should().Be("25h 15m");
	}
}