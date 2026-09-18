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
}
