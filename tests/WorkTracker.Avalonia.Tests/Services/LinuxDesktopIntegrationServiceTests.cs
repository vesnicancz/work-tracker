using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Avalonia.Services.Linux;

namespace WorkTracker.Avalonia.Tests.Services;

public class LinuxDesktopIntegrationServiceTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"worktracker-desktop-{Guid.NewGuid():N}");

	private string ApplicationsDirectory => Path.Combine(_root, "applications");
	private string IconsDirectory => Path.Combine(_root, "icons");

	private string EntryPath => Path.Combine(ApplicationsDirectory, DesktopEntry.FileName);

	private LinuxDesktopIntegrationService CreateService() => new(
		NullLogger<LinuxDesktopIntegrationService>.Instance,
		ApplicationsDirectory,
		IconsDirectory,
		refreshDesktopCaches: false);

	[Fact]
	public void Install_WritesTheDesktopEntryAndTheHicolorIcons()
	{
		CreateService().Install("/home/user/WorkTracker", TestContext.Current.CancellationToken);

		File.ReadAllText(EntryPath).Should().Be(DesktopEntry.BuildApplicationEntry("/home/user/WorkTracker"));

		foreach (var size in new[] { 48, 128, 256 })
		{
			var icon = Path.Combine(IconsDirectory, "hicolor", $"{size}x{size}", "apps", $"{DesktopEntry.AppId}.png");
			File.Exists(icon).Should().BeTrue($"the {size}px icon should be installed");
			new FileInfo(icon).Length.Should().BeGreaterThan(0);
		}
	}

	[Fact]
	public void Install_OnAnOrdinaryLaunch_LeavesTheExistingFilesAlone()
	{
		var service = CreateService();
		service.Install("/home/user/WorkTracker", TestContext.Current.CancellationToken);
		var writtenAt = File.GetLastWriteTimeUtc(EntryPath);

		service.Install("/home/user/WorkTracker", TestContext.Current.CancellationToken);

		File.GetLastWriteTimeUtc(EntryPath).Should().Be(writtenAt);
	}

	/// <summary>The Exec path moves when the user unpacks an update somewhere else.</summary>
	[Fact]
	public void Install_AfterTheExecutableMoved_RewritesTheEntry()
	{
		var service = CreateService();
		service.Install("/home/user/WorkTracker", TestContext.Current.CancellationToken);

		service.Install("/home/user/apps/WorkTracker", TestContext.Current.CancellationToken);

		File.ReadAllText(EntryPath).Should().Contain("Exec=\"/home/user/apps/WorkTracker\"");
	}

	[Theory]
	[InlineData("/usr/bin/WorkTracker", true)]
	[InlineData("/opt/worktracker/WorkTracker", true)]
	[InlineData("/home/user/.local/share/worktracker/WorkTracker", false)]
	public void IsSystemLocation_DetectsPackageManagedCopies(string processPath, bool expected)
	{
		LinuxDesktopIntegrationService.IsSystemLocation(processPath).Should().Be(expected);
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}
		catch (IOException)
		{
			// Best effort — the temp directory is disposable either way.
		}
	}
}
