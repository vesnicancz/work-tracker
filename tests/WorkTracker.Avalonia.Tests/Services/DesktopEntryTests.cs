using FluentAssertions;
using WorkTracker.Avalonia.Services.Linux;

namespace WorkTracker.Avalonia.Tests.Services;

public class DesktopEntryTests
{
	[Theory]
	[InlineData("/home/user/WorkTracker", "\"/home/user/WorkTracker\"")]
	[InlineData("/home/user/My Apps/WorkTracker", "\"/home/user/My Apps/WorkTracker\"")]
	[InlineData("/home/us\"er/WorkTracker", "\"/home/us\\\"er/WorkTracker\"")]
	[InlineData("/home/$USER/WorkTracker", "\"/home/\\$USER/WorkTracker\"")]
	[InlineData("/home/user/back\\slash", "\"/home/user/back\\\\slash\"")]
	[InlineData("/home/user/100%/WorkTracker", "\"/home/user/100%%/WorkTracker\"")]
	[InlineData("/home/user/`cmd`", "\"/home/user/\\`cmd\\`\"")]
	public void EscapeExec_QuotesAndEscapesPerTheDesktopEntrySpec(string path, string expected)
	{
		DesktopEntry.EscapeExec(path).Should().Be(expected);
	}

	/// <summary>
	/// The app id has to be identical in the file name, the Icon key and StartupWMClass, or the
	/// window manager cannot pair a window with the installed entry.
	/// </summary>
	[Fact]
	public void BuildApplicationEntry_TiesTheEntryToTheAppId()
	{
		var entry = DesktopEntry.BuildApplicationEntry("/home/user/WorkTracker");

		entry.Should().StartWith("[Desktop Entry]");
		entry.Should().Contain("Type=Application");
		entry.Should().Contain($"Icon={DesktopEntry.AppId}");
		entry.Should().Contain($"StartupWMClass={DesktopEntry.AppId}");
		entry.Should().Contain("Exec=\"/home/user/WorkTracker\"");
		DesktopEntry.FileName.Should().Be($"{DesktopEntry.AppId}.desktop");
	}

	[Fact]
	public void BuildAutostartEntry_AddsTheSessionManagerKeysToTheApplicationEntry()
	{
		var execPath = "/home/user/WorkTracker";

		var entry = DesktopEntry.BuildAutostartEntry(execPath);

		entry.Should().StartWith(DesktopEntry.BuildApplicationEntry(execPath));
		entry.Should().Contain("X-GNOME-Autostart-enabled=true");
		entry.Should().Contain("Hidden=false");
	}

	[Fact]
	public void BuildApplicationEntry_HasOneKeyPerLine()
	{
		var lines = DesktopEntry.BuildAutostartEntry("/home/user/WorkTracker")
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.TrimEnd('\r'))
			.ToList();

		lines.Should().OnlyContain(line => line == "[Desktop Entry]" || line.Contains('='));
		lines.Should().OnlyContain(line => !line.StartsWith(' ') && !line.StartsWith('\t'));
	}
}
