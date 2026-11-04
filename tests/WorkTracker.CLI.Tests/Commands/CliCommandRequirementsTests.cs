using FluentAssertions;
using Moq;
using WorkTracker.CLI.Commands;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.CLI.Tests.Commands;

public class CliCommandRequirementsTests
{
	private static readonly CliCommandRequirements Send = CliCommandRequirements.Of("send")!.Value;
	private static readonly CliCommandRequirements Providers = CliCommandRequirements.Of("providers")!.Value;

	private static IWorklogUploadPlugin UploadPlugin(string id)
	{
		var plugin = new Mock<IWorklogUploadPlugin>();
		plugin.Setup(p => p.Metadata).Returns(new PluginMetadata { Id = id, Name = id, Version = new Version(1, 0), Author = "Test" });
		return plugin.Object;
	}

	[Theory]
	[InlineData("start")]
	[InlineData("stop")]
	[InlineData("status")]
	[InlineData("list")]
	[InlineData("edit")]
	[InlineData("delete")]
	public void WorkEntryCommands_NeedOnlyTheDatabase(string command)
	{
		CliCommandRequirements.Of(command).Should().Be(new CliCommandRequirements(Database: true, Plugins: CliPluginUse.None));
	}

	[Fact]
	public void Send_NeedsTheDatabaseAndTheUploadPlugin()
	{
		Send.Should().Be(new CliCommandRequirements(Database: true, Plugins: CliPluginUse.Upload));
	}

	[Fact]
	public void Providers_NeedsOnlyTheListOfPlugins()
	{
		Providers.Should().Be(new CliCommandRequirements(Database: false, Plugins: CliPluginUse.Listing));
	}

	[Theory]
	[InlineData("nonsense")]
	[InlineData("")]
	public void UnknownCommand_HasNoRequirements(string command)
	{
		CliCommandRequirements.Of(command).Should().BeNull();
	}

	[Fact]
	public void Providers_StartsNothing()
	{
		Providers.ShouldStart(UploadPlugin("tempo.worklog"), null, enabledUploadPlugins: 1).Should().BeFalse();
	}

	[Fact]
	public void Send_DoesNotStartPluginsThatDoNotUpload()
	{
		// A status indicator started from here would take over the LED of a running GUI
		var statusIndicator = new Mock<IStatusIndicatorPlugin>().Object;
		var suggestions = new Mock<IWorkSuggestionPlugin>().Object;

		Send.ShouldStart(statusIndicator, null, enabledUploadPlugins: 1).Should().BeFalse();
		Send.ShouldStart(suggestions, null, enabledUploadPlugins: 1).Should().BeFalse();
	}

	[Fact]
	public void Send_WithoutProvider_StartsTheOnlyUploadPlugin()
	{
		Send.ShouldStart(UploadPlugin("tempo.worklog"), null, enabledUploadPlugins: 1).Should().BeTrue();
	}

	[Fact]
	public void Send_WithoutProviderAndSeveralEnabled_StartsNone()
	{
		// The command is going to fail, and connecting to each of them first would only delay that
		Send.ShouldStart(UploadPlugin("tempo.worklog"), null, enabledUploadPlugins: 2).Should().BeFalse();
	}

	[Fact]
	public void Send_WithProvider_StartsOnlyThatOne()
	{
		Send.ShouldStart(UploadPlugin("tempo.worklog"), "tempo.worklog", enabledUploadPlugins: 2).Should().BeTrue();
		Send.ShouldStart(UploadPlugin("gorang3.worklog"), "tempo.worklog", enabledUploadPlugins: 2).Should().BeFalse();
	}
}
