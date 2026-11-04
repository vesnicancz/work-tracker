using FluentAssertions;

namespace WorkTracker.Plugin.GoranG3.Tests;

public class GoranG3ParsingTests
{
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
}
