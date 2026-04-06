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
	[InlineData("-15")]
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

	// --- BuildText tests ---

	[Fact]
	public void BuildText_TicketAndDescription_JoinsWithDash()
	{
		GoranG3WorklogPlugin.BuildText("PROJ-1", "Fix bug").Should().Be("PROJ-1 - Fix bug");
	}

	[Fact]
	public void BuildText_OnlyTicketId_ReturnsTicketId()
	{
		GoranG3WorklogPlugin.BuildText("PROJ-1", null).Should().Be("PROJ-1");
	}

	[Fact]
	public void BuildText_OnlyDescription_ReturnsDescription()
	{
		GoranG3WorklogPlugin.BuildText(null, "Fix bug").Should().Be("Fix bug");
	}

	[Fact]
	public void BuildText_BothNull_ReturnsEmptyString()
	{
		GoranG3WorklogPlugin.BuildText(null, null).Should().BeEmpty();
	}

	// --- ParseTags tests ---

	[Fact]
	public void ParseTags_CommaSeparated_SplitsAndTrims()
	{
		var result = GoranG3WorklogPlugin.ParseTags("tag1, tag2, tag3");

		result.Should().NotBeNull();
		result.Should().Equal("tag1", "tag2", "tag3");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void ParseTags_NullOrEmpty_ReturnsNull(string? input)
	{
		GoranG3WorklogPlugin.ParseTags(input).Should().BeNull();
	}

	// --- ParseExternalId tests ---

	[Fact]
	public void ParseExternalId_TicketWithDash_ExtractsNumber()
	{
		GoranG3WorklogPlugin.ParseExternalId("PROJ-123").Should().Be(123);
	}

	[Fact]
	public void ParseExternalId_PureNumber_ReturnsNumber()
	{
		GoranG3WorklogPlugin.ParseExternalId("456").Should().Be(456);
	}

	[Fact]
	public void ParseExternalId_NoNumber_ReturnsNull()
	{
		GoranG3WorklogPlugin.ParseExternalId("PROJ").Should().BeNull();
	}

	[Fact]
	public void ParseExternalId_Null_ReturnsNull()
	{
		GoranG3WorklogPlugin.ParseExternalId(null).Should().BeNull();
	}

	// --- ParseTimesheetResponseCore tests ---

	[Fact]
	public void ParseTimesheetResponseCore_ValidTable_ParsesAllLines()
	{
		var response = """
			Date       | Project | Phase | Description | Start | Duration | Status   | Tags
			-----------|---------|-------|-------------|-------|----------|----------|-----
			2025-01-15 | 000.GOR | SP    | Bug fix     | 9:00  | 60       | Approved | review
			2025-01-15 | 000.GOR | SP    | Meeting     | 10:00 | 30       | Draft    |
			Total: 90 minutes
			""";

		var (worklogs, failedLines) = GoranG3WorklogPlugin.ParseTimesheetResponseCore(response);

		failedLines.Should().BeEmpty();
		worklogs.Should().HaveCount(2);
		worklogs[0].StartTime.Should().Be(new DateTime(2025, 1, 15, 9, 0, 0));
		worklogs[0].DurationMinutes.Should().Be(60);
		worklogs[1].StartTime.Should().Be(new DateTime(2025, 1, 15, 10, 0, 0));
		worklogs[1].DurationMinutes.Should().Be(30);
	}

	[Fact]
	public void ParseTimesheetResponseCore_EmptyResponse_ReturnsEmpty()
	{
		var (worklogs, failedLines) = GoranG3WorklogPlugin.ParseTimesheetResponseCore("");

		worklogs.Should().BeEmpty();
		failedLines.Should().BeEmpty();
	}
}
