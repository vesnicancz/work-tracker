namespace WorkTracker.Avalonia.Services.Linux;

/// <summary>
/// The XDG base directories the desktop integration writes into, with the fallbacks the
/// freedesktop.org base directory specification prescribes when the variables are unset.
/// </summary>
internal static class XdgDirectories
{
	internal static string ConfigHome => Resolve("XDG_CONFIG_HOME", ".config");

	internal static string DataHome => Resolve("XDG_DATA_HOME", Path.Combine(".local", "share"));

	internal static string AutostartDirectory => Path.Combine(ConfigHome, "autostart");

	internal static string ApplicationsDirectory => Path.Combine(DataHome, "applications");

	internal static string IconsDirectory => Path.Combine(DataHome, "icons");

	private static string Resolve(string variable, string relativeFallback)
	{
		var value = Environment.GetEnvironmentVariable(variable);
		return string.IsNullOrEmpty(value)
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), relativeFallback)
			: value;
	}
}
