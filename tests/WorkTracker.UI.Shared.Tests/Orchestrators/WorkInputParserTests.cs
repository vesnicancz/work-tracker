using FluentAssertions;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared.Tests.Orchestrators;

public class WorkInputParserTests
{
	[Fact]
	public void Parse_Null_ReturnsBothNull()
	{
		var (ticketId, description) = WorkInputParser.Parse(null);
		ticketId.Should().BeNull();
		description.Should().BeNull();
	}

	[Fact]
	public void Parse_Empty_ReturnsBothNull()
	{
		var (ticketId, description) = WorkInputParser.Parse("");
		ticketId.Should().BeNull();
		description.Should().BeNull();
	}

	[Fact]
	public void Parse_Whitespace_ReturnsBothNull()
	{
		var (ticketId, description) = WorkInputParser.Parse("   ");
		ticketId.Should().BeNull();
		description.Should().BeNull();
	}

	[Fact]
	public void Parse_TicketOnly_ReturnsTicketAndNullDescription()
	{
		var (ticketId, description) = WorkInputParser.Parse("PROJ-123");
		ticketId.Should().Be("PROJ-123");
		description.Should().BeNull();
	}

	[Fact]
	public void Parse_TicketWithDescription_ReturnsBoth()
	{
		var (ticketId, description) = WorkInputParser.Parse("PROJ-123 Working on feature");
		ticketId.Should().Be("PROJ-123");
		description.Should().Be("Working on feature");
	}

	[Fact]
	public void Parse_DescriptionOnly_ReturnsNullTicket()
	{
		var (ticketId, description) = WorkInputParser.Parse("Just a description");
		ticketId.Should().BeNull();
		description.Should().Be("Just a description");
	}

	[Fact]
	public void Parse_TicketWithExtraSpaces_TrimsDescription()
	{
		var (ticketId, description) = WorkInputParser.Parse("ABC-1   spaced out");
		ticketId.Should().Be("ABC-1");
		description.Should().Be("spaced out");
	}

	[Fact]
	public void Parse_LowercaseTicket_StillMatches()
	{
		var (ticketId, description) = WorkInputParser.Parse("proj-123 desc");
		ticketId.Should().Be("proj-123");
		description.Should().Be("desc");
	}
}