using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

public sealed class AutostartManager : IAutostartManager
{
	private readonly ILogger<AutostartManager> _logger;

	public AutostartManager(ILogger<AutostartManager> logger)
	{
		_logger = logger;
	}

	public bool IsEnabled
	{
		get
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return GetWindowsAutostart();
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				return GetLinuxAutostart();
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				return GetMacOSAutostart();
			}
			return false;
		}
	}

	public void SetAutostart(bool enable)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			SetWindowsAutostart(enable);
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			SetLinuxAutostart(enable);
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			SetMacOSAutostart(enable);
		}
	}

	private bool GetWindowsAutostart()
	{
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}

			using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
			var value = key?.GetValue("WorkTracker") as string;
			return !string.IsNullOrEmpty(value);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to check autostart status");
			return false;
		}
	}

	private void SetWindowsAutostart(bool enable)
	{
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				return;
			}

			using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
			if (key == null)
			{
				return;
			}

			if (enable)
			{
				var processPath = Environment.ProcessPath;
				if (!string.IsNullOrEmpty(processPath))
				{
					key.SetValue("WorkTracker", $"\"{processPath}\"");
					_logger.LogInformation("Autostart enabled: {Path}", processPath);
				}
			}
			else
			{
				key.DeleteValue("WorkTracker", false);
				_logger.LogInformation("Autostart disabled");
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to set autostart to {Enable}", enable);
		}
	}

	#region Linux

	private static string LinuxDesktopFilePath =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart", "WorkTracker.desktop");

	private bool GetLinuxAutostart()
	{
		try
		{
			return File.Exists(LinuxDesktopFilePath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to check Linux autostart status");
			return false;
		}
	}

	private void SetLinuxAutostart(bool enable)
	{
		try
		{
			if (enable)
			{
				var processPath = Environment.ProcessPath;
				if (string.IsNullOrEmpty(processPath))
				{
					_logger.LogWarning("Unable to enable Linux autostart: Environment.ProcessPath is null or empty");
					return;
				}

				var dir = Path.GetDirectoryName(LinuxDesktopFilePath)!;
				Directory.CreateDirectory(dir);
				File.WriteAllText(LinuxDesktopFilePath,
$"""
[Desktop Entry]
Type=Application
Name=WorkTracker
Exec="{processPath}"
X-GNOME-Autostart-enabled=true
""");
				_logger.LogInformation("Linux autostart enabled: {Path}", processPath);
			}
			else
			{
				if (File.Exists(LinuxDesktopFilePath))
				{
					File.Delete(LinuxDesktopFilePath);
				}
				_logger.LogInformation("Linux autostart disabled");
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to set Linux autostart to {Enable}", enable);
		}
	}

	#endregion Linux

	#region macOS

	private static string MacOSPlistFilePath =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "com.worktracker.plist");

	private bool GetMacOSAutostart()
	{
		try
		{
			return File.Exists(MacOSPlistFilePath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to check macOS autostart status");
			return false;
		}
	}

	private void SetMacOSAutostart(bool enable)
	{
		try
		{
			if (enable)
			{
				var processPath = Environment.ProcessPath;
				if (string.IsNullOrEmpty(processPath))
				{
					_logger.LogWarning("Unable to enable macOS autostart: Environment.ProcessPath is null or empty");
					return;
				}

				var dir = Path.GetDirectoryName(MacOSPlistFilePath)!;
				Directory.CreateDirectory(dir);
				File.WriteAllText(MacOSPlistFilePath,
$"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>Label</key>
	<string>com.worktracker</string>
	<key>ProgramArguments</key>
	<array>
		<string>{SecurityElement.Escape(processPath)}</string>
	</array>
	<key>RunAtLoad</key>
	<true/>
</dict>
</plist>
""");
				_logger.LogInformation("macOS autostart enabled: {Path}", processPath);
			}
			else
			{
				if (File.Exists(MacOSPlistFilePath))
				{
					File.Delete(MacOSPlistFilePath);
				}
				_logger.LogInformation("macOS autostart disabled");
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to set macOS autostart to {Enable}", enable);
		}
	}

	#endregion macOS
}