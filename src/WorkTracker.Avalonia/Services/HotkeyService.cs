using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Global hotkey service for Avalonia.
/// Windows: Win32 RegisterHotKey via native window handle.
/// Linux/macOS: not yet implemented (Register is no-op on unsupported platforms).
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
	private readonly ILogger<HotkeyService> _logger;
	private bool _isRegistered;

	public event EventHandler? HotkeyPressed;

	public HotkeyService(ILogger<HotkeyService> logger)
	{
		_logger = logger;
	}

	public void Register()
	{
		if (_isRegistered) return;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			RegisterWindows();
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			// TODO: Implement via X11 global key grab or DBus GlobalShortcuts portal
			_logger.LogInformation("Global hotkeys not yet implemented on Linux");
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			// TODO: Implement via CGEventTap or NSEvent.addGlobalMonitorForEvents
			_logger.LogInformation("Global hotkeys not yet implemented on macOS");
		}
	}

	public void Unregister()
	{
		if (!_isRegistered) return;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			UnregisterWindows();
		}
	}

	public void Dispose()
	{
		Unregister();
	}

	#region Windows implementation

	private const int HotkeyId = 9000;
	private const uint ModAlt = 0x0001;
	private const uint ModControl = 0x0002;
	private const uint ModShift = 0x0004;
	private const uint VkW = 0x57; // 'W'
	private const int WmHotkey = 0x0312;

	private IntPtr _windowHandle;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	private void RegisterWindows()
	{
		var mainWindow = GetMainWindow();
		var handle = mainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
		if (mainWindow == null || handle == IntPtr.Zero)
		{
			_logger.LogWarning("Cannot register hotkey: main window handle not available");
			return;
		}

		_windowHandle = handle;

		if (!RegisterHotKey(_windowHandle, HotkeyId, ModControl | ModShift, VkW))
		{
			var error = Marshal.GetLastWin32Error();
			_logger.LogWarning("Failed to register Ctrl+Shift+W hotkey (error {Error})", error);
			return;
		}

		// Hook into Avalonia's Win32 message loop
		Win32Properties.AddWndProcHookCallback(mainWindow, WndProcHook);

		_isRegistered = true;
		_logger.LogInformation("Registered global hotkey Ctrl+Shift+W");
	}

	private void UnregisterWindows()
	{
		if (_windowHandle != IntPtr.Zero)
		{
			UnregisterHotKey(_windowHandle, HotkeyId);

			var mainWindow = GetMainWindow();
			if (mainWindow != null)
			{
				Win32Properties.RemoveWndProcHookCallback(mainWindow, WndProcHook);
			}

			_windowHandle = IntPtr.Zero;
		}

		_isRegistered = false;
		_logger.LogInformation("Unregistered global hotkey");
	}

	private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
		{
			HotkeyPressed?.Invoke(this, EventArgs.Empty);
			handled = true;
		}

		return IntPtr.Zero;
	}

	private static IntPtr GetMainWindowHandle()
	{
		var mainWindow = GetMainWindow();
		return mainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
	}

	private static Window? GetMainWindow()
	{
		return (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
	}

	#endregion
}
