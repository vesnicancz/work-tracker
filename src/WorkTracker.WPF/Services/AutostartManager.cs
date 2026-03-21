using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.WPF.Services;

/// <summary>
/// Manages application autostart with Windows
/// </summary>
public sealed class AutostartManager : IAutostartManager
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
	private static string GetExecutablePath()
	{
		return Environment.ProcessPath
			?? Path.Combine(AppContext.BaseDirectory, "WorkTracker.WPF.exe");
	}
}
