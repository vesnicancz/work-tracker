using System.Text;

namespace WorkTracker.Avalonia.Services.Linux;

/// <summary>
/// Builds freedesktop.org desktop entries for the application and for the autostart directory.
/// <para>
/// <see cref="AppId"/> is the single identity the desktop uses to tie things together: it names the
/// .desktop file, the icon in the hicolor theme, the window's WM_CLASS (see
/// <c>X11PlatformOptions.WmClass</c> in <c>Program.cs</c>) and the entry's StartupWMClass. All four
/// must match, or the window manager cannot pair the window with the application and falls back to a
/// generic icon and the raw process name.
/// </para>
/// </summary>
internal static class DesktopEntry
{
	/// <summary>Reverse-DNS application id, derived from the project's GitHub home.</summary>
	internal const string AppId = "io.github.vesnicancz.WorkTracker";

	internal const string FileName = $"{AppId}.desktop";

	/// <summary>Name of the entry written by earlier versions, cleaned up on upgrade.</summary>
	internal const string LegacyFileName = "WorkTracker.desktop";

	/// <summary>
	/// The entry installed into the applications directory: puts the app in the desktop's menu and
	/// gives its window an icon and a name.
	/// </summary>
	internal static string BuildApplicationEntry(string execPath) =>
		$"""
		[Desktop Entry]
		Type=Application
		Name=WorkTracker
		GenericName=Work time tracker
		Comment=Track work time and submit worklogs
		Comment[cs]=Sledování odpracovaného času a odesílání worklogů
		Exec={EscapeExec(execPath)}
		Icon={AppId}
		Terminal=false
		Categories=Office;ProjectManagement;
		Keywords=time;tracking;worklog;timesheet;jira;tempo;
		StartupWMClass={AppId}

		""";

	/// <summary>
	/// The entry installed into the autostart directory. Same as the application entry plus the keys
	/// the session managers read to decide whether to launch it.
	/// </summary>
	internal static string BuildAutostartEntry(string execPath) =>
		BuildApplicationEntry(execPath) +
		"""
		X-GNOME-Autostart-enabled=true
		Hidden=false

		""";

	/// <summary>
	/// Quotes a program path for the Exec key. The spec reserves a set of characters inside a quoted
	/// argument that have to be backslash-escaped, and treats '%' as the start of a field code, so a
	/// literal one is written as '%%'. Without this, a path containing a space, a quote or a dollar
	/// sign produces an entry the session manager refuses to launch.
	/// </summary>
	internal static string EscapeExec(string execPath)
	{
		var escaped = new StringBuilder(execPath.Length + 8);
		escaped.Append('"');

		foreach (var c in execPath)
		{
			switch (c)
			{
				case '"':
				case '`':
				case '$':
				case '\\':
					escaped.Append('\\').Append(c);
					break;
				case '%':
					escaped.Append("%%");
					break;
				default:
					escaped.Append(c);
					break;
			}
		}

		return escaped.Append('"').ToString();
	}
}
