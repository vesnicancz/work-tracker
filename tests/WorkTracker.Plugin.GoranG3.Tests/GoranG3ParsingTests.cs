using FluentAssertions;

namespace WorkTracker.Plugin.GoranG3.Tests;

public class GoranG3ParsingTests
{
	[Theory]
	[InlineData("60", 60)]
	[InlineData("30", 30)]
	[InlineData("120", 120)]
	[InlineData("0", 0)]
	public void TryParseDuration_IntegerMinutes_ReturnsCorrectValue(string input, int expected)
	{
		GoranG3WorklogPlugin.TryParseDuration(input, out var minutes).Should().BeTrue();
		minutes.Should().Be(expected);
	}

	[Theory]
	[InlineData("1:30", 90)]
	[InlineData("2:00", 120)]
	[InlineData("0:45", 45)]
	public void TryParseDuration_ColonFormat_ReturnsCorrectValue(string input, int expected)
	{
		GoranG3WorklogPlugin.TryParseDuration(input, out var minutes).Should().BeTrue();
		minutes.Should().Be(expected);
	}

	[Theory]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("1h30m")]
	public void TryParseDuration_InvalidInput_ReturnsFalse(string input)
	{
		GoranG3WorklogPlugin.TryParseDuration(input, out _).Should().BeFalse();
	}

	[Fact]
	public void ParseTimesheetLine_ValidPipeDelimitedLine_ReturnsEntry()
	{
		var line = "2025-01-15 | 000.GOR | SP | Bug fix | 9:00 | 60 | Approved | review";

		var entry = GoranG3WorklogPlugin.ParseTimesheetLine(line);

		entry.Should().NotBeNull();
		entry!.StartTime.Date.Should().Be(new DateTime(2025, 1, 15));
		entry.StartTime.Hour.Should().Be(9);
		entry.StartTime.Minute.Should().Be(0);
		entry.DurationMinutes.Should().Be(60);
	}

	[Fact]
	public void ParseTimesheetLine_ColonDuration_ReturnsEntry()
	{
		var line = "2025-01-15 | 000.GOR | SP | Meeting | 14:00 | 1:30 | Draft | ";

		var entry = GoranG3WorklogPlugin.ParseTimesheetLine(line);

		entry.Should().NotBeNull();
		entry!.DurationMinutes.Should().Be(90);
	}

	[Fact]
	public void ParseTimesheetLine_TooFewColumns_ReturnsNull()
	{
		var line = "2025-01-15 | 60";

		GoranG3WorklogPlugin.ParseTimesheetLine(line).Should().BeNull();
	}

	[Fact]
	public void ParseTimesheetLine_NoDate_ReturnsNull()
	{
		var line = "abc | 000.GOR | SP | Bug fix | def";

		GoranG3WorklogPlugin.ParseTimesheetLine(line).Should().BeNull();
	}

	[Fact]
	public void ParseTimesheetLine_NoDuration_ReturnsNull()
	{
		var line = "2025-01-15 | 000.GOR | SP | Bug fix | nodur";

		GoranG3WorklogPlugin.ParseTimesheetLine(line).Should().BeNull();
	}

	[Fact]
	public void ParseTimesheetLine_NoStartTime_ReturnsNull()
	{
		var line = "2025-01-15 | 000.GOR | SP | Bug fix | notime | 60 | Approved | ";

		GoranG3WorklogPlugin.ParseTimesheetLine(line).Should().BeNull();
	}

	[Fact]
	public void ParseTimesheetLine_SetsEndTimeFromDuration()
	{
		var line = "2025-03-10 | PROJ | DEV | Work item | 10:00 | 45 | Approved | ";

		var entry = GoranG3WorklogPlugin.ParseTimesheetLine(line);

		entry.Should().NotBeNull();
		entry!.EndTime.Should().Be(entry.StartTime.AddMinutes(45));
	}
}
