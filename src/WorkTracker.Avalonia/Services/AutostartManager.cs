using System.Runtime.InteropServices;
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
			// TODO: Linux/macOS support
			return false;
		}
	}

	public void SetAutostart(bool enable)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			SetWindowsAutostart(enable);
		}
		// TODO: Linux/macOS support
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
}
