using FluentAssertions;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class HotkeyServiceTests
{
	/// <summary>
	/// The trigger is handed to the desktop in the freedesktop.org shortcuts syntax: modifiers out
	/// of CTRL/ALT/SHIFT/NUM/LOGO in upper case, then a key named after its xkbcommon keysym. The
	/// keysym for the W key is a lowercase 'w' regardless of Shift, and an upper-case 'W' would
	/// either bind the wrong key or be rejected.
	/// </summary>
	[Fact]
	public void NewWorkEntryTrigger_UsesTheShortcutsSpecSyntax()
	{
		HotkeyService.NewWorkEntryTrigger.Should().Be("CTRL+SHIFT+w");
	}

	/// <summary>
	/// The desktop files a bound shortcut under this id and remembers it across restarts, so
	/// changing it would silently orphan every user's existing binding.
	/// </summary>
	[Fact]
	public void NewWorkEntryShortcutId_IsStable()
	{
		HotkeyService.NewWorkEntryShortcutId.Should().Be("new-work-entry");
	}
}
