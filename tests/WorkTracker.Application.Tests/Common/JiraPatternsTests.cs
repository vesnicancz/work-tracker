using FluentAssertions;

namespace WorkTracker.Application.Tests.Common;

public class JiraPatternsTests
{
	[Theory]
	[InlineData("PROJ-123", "PROJ-123")]
	[InlineData("ABC-1", "ABC-1")]
	[InlineData("X-999", "X-999")]
	[InlineData("TEAM2-42", "TEAM2-42")]
	[InlineData("A1B2-100", "A1B2-100")]
	public void TicketId_ValidPatterns_MatchesCorrectly(string input, string expected)
	{
		var match = Application.Common.JiraPatterns.TicketId().Match(input);

		match.Success.Should().BeTrue();
		match.Groups[1].Value.Should().Be(expected);
	}

	[Theory]
	[InlineData("PROJ-123 some description", "PROJ-123")]
	[InlineData("ABC-1 working on feature", "ABC-1")]
	public void TicketId_WithTrailingText_MatchesTicketPart(string input, string expected)
	{
		var match = Application.Common.JiraPatterns.TicketId().Match(input);

		match.Success.Should().BeTrue();
		match.Groups[1].Value.Should().Be(expected);
	}

	[Theory]
	[InlineData("just a description")]
	[InlineData("no ticket here")]
	[InlineData("-123")]
	[InlineData("PROJ-")]
	[InlineData("PROJ")]
	[InlineData("")]
	public void TicketId_InvalidPatterns_DoesNotMatch(string input)
	{
		var match = Application.Common.JiraPatterns.TicketId().Match(input);

		match.Success.Should().BeFalse();
	}

	[Theory]
	[InlineData("proj-123", "proj-123")]
	[InlineData("Proj-456", "Proj-456")]
	public void TicketId_CaseInsensitive_Matches(string input, string expected)
	{
		var match = Application.Common.JiraPatterns.TicketId().Match(input);

		match.Success.Should().BeTrue();
		match.Groups[1].Value.Should().Be(expected);
	}

	[Fact]
	public void TicketId_MustBeAtBeginning()
	{
		var match = Application.Common.JiraPatterns.TicketId().Match("description PROJ-123");

		match.Success.Should().BeFalse();
	}
}
