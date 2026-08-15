using FluentAssertions;
using WorkTracker.CLI.Commands;

namespace WorkTracker.CLI.Tests.Commands;

public class CliArgumentParserTests
{
	#region ParseDateTime

	[Theory]
	[InlineData("14:30")]
	[InlineData("14:30:00")]
	public void ParseDateTime_TimeOnly_CombinesWithToday(string input)
	{
		var result = CliArgumentParser.ParseDateTime(input);

		result.Should().NotBeNull();
		result!.Value.Date.Should().Be(DateTime.Today);
		result.Value.Hour.Should().Be(14);
		result.Value.Minute.Should().Be(30);
	}

	[Fact]
	public void ParseDateTime_FullDateTime_ReturnsExactValue()
	{
		var result = CliArgumentParser.ParseDateTime("2025-10-30 14:30");

		result.Should().Be(new DateTime(2025, 10, 30, 14, 30, 0));
	}

	[Theory]
	[InlineData("notatime")]
	[InlineData("25:99")]
	[InlineData("")]
	public void ParseDateTime_Invalid_ReturnsNull(string input)
	{
		CliArgumentParser.ParseDateTime(input).Should().BeNull();
	}

	#endregion ParseDateTime

	#region ParseStartCommandInput

	[Fact]
	public void ParseStart_TicketOnly()
	{
		var (ticketId, description, startTime) =
			CliArgumentParser.ParseStartCommandInput(["start", "PROJ-123"]);

		ticketId.Should().Be("PROJ-123");
		description.Should().BeNull();
		startTime.Should().BeNull();
	}

	[Fact]
	public void ParseStart_TicketAndDescription()
	{
		var (ticketId, description, startTime) =
			CliArgumentParser.ParseStartCommandInput(["start", "PROJ-123", "Working", "on", "auth"]);

		ticketId.Should().Be("PROJ-123");
		description.Should().Be("Working on auth");
		startTime.Should().BeNull();
	}

	[Fact]
	public void ParseStart_TicketDescriptionAndTime()
	{
		var (ticketId, description, startTime) =
			CliArgumentParser.ParseStartCommandInput(["start", "PROJ-123", "Bug", "fix", "09:00"]);

		ticketId.Should().Be("PROJ-123");
		description.Should().Be("Bug fix");
		startTime.Should().Be(DateTime.Today.AddHours(9));
	}

	[Fact]
	public void ParseStart_TicketAndTimeWithoutDescription()
	{
		var (ticketId, description, startTime) =
			CliArgumentParser.ParseStartCommandInput(["start", "PROJ-123", "09:00"]);

		ticketId.Should().Be("PROJ-123");
		description.Should().BeNull();
		startTime.Should().Be(DateTime.Today.AddHours(9));
	}

	[Fact]
	public void ParseStart_DescriptionOnly_NoTicketDetected()
	{
		var (ticketId, description, startTime) =
			CliArgumentParser.ParseStartCommandInput(["start", "Working on documentation"]);

		ticketId.Should().BeNull();
		description.Should().Be("Working on documentation");
		startTime.Should().BeNull();
	}

	[Fact]
	public void ParseStart_DescriptionWithFullDateTime_UsesLastTwoTokensAsTime()
	{
		var (ticketId, description, startTime) =
			CliArgumentParser.ParseStartCommandInput(["start", "Documentation", "2025-10-30", "09:00"]);

		ticketId.Should().BeNull();
		description.Should().Be("Documentation");
		startTime.Should().Be(new DateTime(2025, 10, 30, 9, 0, 0));
	}

	[Fact]
	public void ParseStart_LowercaseTicket_IsDetected()
	{
		var (ticketId, description, _) =
			CliArgumentParser.ParseStartCommandInput(["start", "proj-123", "fix"]);

		ticketId.Should().Be("proj-123");
		description.Should().Be("fix");
	}

	#endregion ParseStartCommandInput

	#region ParseEditOptions

	[Fact]
	public void ParseEdit_AllFlags()
	{
		var options = CliArgumentParser.ParseEditOptions(
			["edit", "5", "--ticket=PROJ-9", "--start=09:00", "--end=17:30", "--desc=New description"],
			out var invalidField);

		invalidField.Should().BeNull();
		options.Should().NotBeNull();
		options!.TicketId.Should().Be("PROJ-9");
		options.StartTime.Should().Be(DateTime.Today.AddHours(9));
		options.EndTime.Should().Be(DateTime.Today.AddHours(17).AddMinutes(30));
		options.Description.Should().Be("New description");
	}

	[Fact]
	public void ParseEdit_NoFlags_ReturnsEmptyOptions()
	{
		var options = CliArgumentParser.ParseEditOptions(["edit", "5"], out var invalidField);

		invalidField.Should().BeNull();
		options.Should().Be(new CliArgumentParser.EditOptions(null, null, null, null));
	}

	[Fact]
	public void ParseEdit_InvalidStart_ReturnsNullWithField()
	{
		var options = CliArgumentParser.ParseEditOptions(["edit", "5", "--start=garbage"], out var invalidField);

		options.Should().BeNull();
		invalidField.Should().Be("start");
	}

	[Fact]
	public void ParseEdit_InvalidEnd_ReturnsNullWithField()
	{
		var options = CliArgumentParser.ParseEditOptions(["edit", "5", "--end=garbage"], out var invalidField);

		options.Should().BeNull();
		invalidField.Should().Be("end");
	}

	[Fact]
	public void ParseEdit_UnknownFlag_IsIgnored()
	{
		var options = CliArgumentParser.ParseEditOptions(
			["edit", "5", "--unknown=x", "--desc=Text"], out var invalidField);

		invalidField.Should().BeNull();
		options!.Description.Should().Be("Text");
		options.TicketId.Should().BeNull();
	}

	#endregion ParseEditOptions

	#region TryParseSendArguments

	[Fact]
	public void ParseSend_NoArguments_DefaultsToTodayNonWeek()
	{
		var ok = CliArgumentParser.TryParseSendArguments(["send"], out var date, out var isWeek);

		ok.Should().BeTrue();
		date.Should().BeNull();
		isWeek.Should().BeFalse();
	}

	[Fact]
	public void ParseSend_Week()
	{
		var ok = CliArgumentParser.TryParseSendArguments(["send", "week"], out var date, out var isWeek);

		ok.Should().BeTrue();
		date.Should().BeNull();
		isWeek.Should().BeTrue();
	}

	[Fact]
	public void ParseSend_WeekWithDate()
	{
		var ok = CliArgumentParser.TryParseSendArguments(["send", "week", "2025-10-30"], out var date, out var isWeek);

		ok.Should().BeTrue();
		date.Should().Be(new DateTime(2025, 10, 30));
		isWeek.Should().BeTrue();
	}

	[Fact]
	public void ParseSend_DateOnly()
	{
		var ok = CliArgumentParser.TryParseSendArguments(["send", "2025-10-30"], out var date, out var isWeek);

		ok.Should().BeTrue();
		date.Should().Be(new DateTime(2025, 10, 30));
		isWeek.Should().BeFalse();
	}

	[Fact]
	public void ParseSend_InvalidDate_Fails()
	{
		var ok = CliArgumentParser.TryParseSendArguments(["send", "garbage"], out _, out var isWeek);

		ok.Should().BeFalse();
		isWeek.Should().BeFalse();
	}

	[Fact]
	public void ParseSend_WeekWithInvalidDate_Fails()
	{
		var ok = CliArgumentParser.TryParseSendArguments(["send", "week", "garbage"], out _, out var isWeek);

		ok.Should().BeFalse();
		isWeek.Should().BeTrue();
	}

	#endregion TryParseSendArguments
}
