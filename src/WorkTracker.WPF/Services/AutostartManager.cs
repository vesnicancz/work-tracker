using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace WorkTracker.WPF.Services;

/// <summary>
/// Manages application autostart with Windows
/// </summary>
public class AutostartManager : IAutostartManager
{
	private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string AppName = "WorkTracker";
	private readonly ILogger<AutostartManager> _logger;

	public AutostartManager(ILogger<AutostartManager> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Gets whether the application is configured to start with Windows
	/// </summary>
	public bool IsEnabled
	{
		get
		{
			try
			{
				using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
				var value = key?.GetValue(AppName) as string;
				return !string.IsNullOrEmpty(value);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to check autostart status");
				return false;
			}
		}
	}

	/// <summary>
	/// Enables or disables application autostart with Windows
	/// </summary>
	/// <param name="enable">True to enable autostart, false to disable</param>
	public void SetAutostart(bool enable)
	{
		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
			if (key == null)
			{
				_logger.LogError("Failed to open registry key for autostart");
				return;
			}

			if (enable)
			{
				var executablePath = GetExecutablePath();
				if (!string.IsNullOrEmpty(executablePath))
				{
					key.SetValue(AppName, $"\"{executablePath}\"");
					_logger.LogInformation("Autostart enabled: {Path}", executablePath);
				}
				else
				{
					_logger.LogError("Failed to get executable path for autostart");
				}
			}
			else
			{
				key.DeleteValue(AppName, false);
				_logger.LogInformation("Autostart disabled");
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to set autostart to {Enable}", enable);
			throw;
		}
	}

	/// <summary>
	/// Gets the path to the application executable
	/// </summary>
	private string GetExecutablePath()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var location = assembly.Location;

		// If we're running as a single-file app, get the actual executable path
		if (string.IsNullOrEmpty(location) || location.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
		{
			// Get the path from the process
			var processPath = Environment.ProcessPath;
			if (!string.IsNullOrEmpty(processPath))
			{
				return processPath;
			}
		}

		// For regular .dll assembly, find the .exe in the same directory
		if (location.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
		{
			var directory = Path.GetDirectoryName(location);
			if (!string.IsNullOrEmpty(directory))
			{
				var exeName = Path.GetFileNameWithoutExtension(location) + ".exe";
				var exePath = Path.Combine(directory, exeName);
				if (File.Exists(exePath))
				{
					return exePath;
				}
			}
		}

		return location;
	}
}
