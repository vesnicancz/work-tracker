using FluentAssertions;
using WorkTracker.CLI.Commands;

namespace WorkTracker.CLI.Tests.Commands;

public class CliCommandRequirementsTests
{
	[Theory]
	[InlineData("start")]
	[InlineData("stop")]
	[InlineData("status")]
	[InlineData("list")]
	[InlineData("edit")]
	[InlineData("delete")]
	public void WorkEntryCommands_NeedOnlyTheDatabase(string command)
	{
		CliCommandRequirements.Of(command).Should().Be(new CliCommandRequirements(Database: true, Plugins: false));
	}

	[Fact]
	public void Send_NeedsTheDatabaseAndPlugins()
	{
		CliCommandRequirements.Of("send").Should().Be(new CliCommandRequirements(Database: true, Plugins: true));
	}

	[Fact]
	public void Providers_NeedsOnlyPlugins()
	{
		CliCommandRequirements.Of("providers").Should().Be(new CliCommandRequirements(Database: false, Plugins: true));
	}

	[Theory]
	[InlineData("nonsense")]
	[InlineData("")]
	public void UnknownCommand_HasNoRequirements(string command)
	{
		CliCommandRequirements.Of(command).Should().BeNull();
	}
}
