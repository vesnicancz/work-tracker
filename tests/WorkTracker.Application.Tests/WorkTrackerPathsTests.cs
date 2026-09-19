using FluentAssertions;

namespace WorkTracker.Application.Tests;

public class WorkTrackerPathsTests
{
	// The macOS .app layout splits shipped content from writable state. The test host is never a
	// bundle, so what is pinned here is the other half of the contract: off macOS — and on macOS
	// outside a bundle — both directories stay the executable directory, exactly as before.
	[Fact]
	public void AppContentDirectory_OutsideAppBundle_ShouldBeExecutableDirectory()
	{
		WorkTrackerPaths.AppContentDirectory.Should().Be(AppContext.BaseDirectory);
	}

	[Fact]
	public void WritableBaseDirectory_OutsideAppBundle_ShouldBeExecutableDirectory()
	{
		WorkTrackerPaths.WritableBaseDirectory.Should().Be(AppContext.BaseDirectory);
	}

	[Fact]
	public void DefaultPluginsPath_ShouldSitUnderWritableBaseDirectory()
	{
		WorkTrackerPaths.DefaultPluginsPath.Should()
			.Be(Path.Combine(WorkTrackerPaths.WritableBaseDirectory, "plugins"));
	}

	#region AppDataDirectory

	// The platform hands back an empty string for a local application data directory it cannot
	// verify — on Unix that is any account whose ~/.local/share does not exist yet. Combining that
	// with a folder name yields a relative path, and the database, the settings and the logs then
	// follow the working directory: a separate, empty set for every place the app is started from.
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void BuildAppDataDirectory_WithNoLocalAppData_StillReturnsARootedPath(string localAppData)
	{
		var path = WorkTrackerPaths.BuildAppDataDirectory(localAppData, environmentName: null);

		Path.IsPathRooted(path).Should().BeTrue("a relative path would follow the working directory");
		path.Should().Be(Path.Combine(AppContext.BaseDirectory, "WorkTracker"));
	}

	[Fact]
	public void BuildAppDataDirectory_WithLocalAppData_SitsUnderIt()
	{
		var path = WorkTrackerPaths.BuildAppDataDirectory(
			Path.Combine(Path.DirectorySeparatorChar.ToString(), "home", "user", ".local", "share"),
			environmentName: null);

		path.Should().Be(Path.Combine(
			Path.DirectorySeparatorChar.ToString(), "home", "user", ".local", "share", "WorkTracker"));
	}

	[Theory]
	[InlineData(null, "WorkTracker")]
	[InlineData("", "WorkTracker")]
	[InlineData("Production", "WorkTracker")]
	[InlineData("production", "WorkTracker")]
	[InlineData("Development", "WorkTracker_Development")]
	[InlineData("Staging", "WorkTracker_Staging")]
	public void BuildAppDataDirectory_SuffixesEveryEnvironmentButProduction(string? environmentName, string expectedFolder)
	{
		var root = Path.Combine(Path.DirectorySeparatorChar.ToString(), "data");

		WorkTrackerPaths.BuildAppDataDirectory(root, environmentName)
			.Should().Be(Path.Combine(root, expectedFolder));
	}

	[Fact]
	public void AppDataDirectory_IsRooted()
	{
		Path.IsPathRooted(WorkTrackerPaths.AppDataDirectory).Should().BeTrue();
	}

	#endregion
}
