using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using WorkTracker.Avalonia.Services.Linux;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Global hotkey service for Avalonia.
/// Windows: Win32 RegisterHotKey via native window handle.
/// Linux: the org.freedesktop.portal.GlobalShortcuts desktop portal, which covers Wayland (where a
/// client cannot grab keys itself) and X11 alike, as long as the desktop implements it.
/// macOS: not yet implemented (Register is a no-op there).
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
	/// <summary>Id the portal reports back in its Activated signal; must stay stable across releases.</summary>
	internal const string NewWorkEntryShortcutId = "new-work-entry";

	/// <summary>
	/// Ctrl+Shift+W in the freedesktop.org shortcuts syntax, matching the Windows binding. The key is
	/// named after its unshifted keysym, so it is a lowercase 'w' even though Shift is held.
	/// </summary>
	internal const string NewWorkEntryTrigger = "CTRL+SHIFT+w";

	private readonly ILogger<HotkeyService> _logger;
	private readonly ILocalizationService _localization;
	private bool _isRegistered;

	public event EventHandler? HotkeyPressed;

	public HotkeyService(ILogger<HotkeyService> logger, ILocalizationService localization)
	{
		_logger = logger;
		_localization = localization;
	}

	public void Register()
	{
		if (_isRegistered)
		{
			return;
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			RegisterWindows();
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			RegisterLinux();
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			// TODO: Implement via CGEventTap or NSEvent.addGlobalMonitorForEvents
			_logger.LogInformation("Global hotkeys not yet implemented on macOS");
		}
	}

	public void Unregister()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			UnregisterLinux();
			return;
		}

		if (!_isRegistered)
		{
			return;
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			UnregisterWindows();
		}
	}

	public void Dispose()
	{
		Unregister();
	}

	#region Linux implementation

	/// <summary>
	/// Portal session, kept for the lifetime of the registration: the shortcut is bound to the D-Bus
	/// connection it was created on and disappears when that connection goes.
	/// </summary>
	private GlobalShortcutsPortal? _portal;

	/// <summary>
	/// Binding goes through D-Bus and may wait on a confirmation dialog, so it cannot happen inside
	/// this synchronous call. Kept so shutdown can wait for it rather than racing it.
	/// </summary>
	private Task? _linuxRegistration;

	private void RegisterLinux()
	{
		var portal = new GlobalShortcutsPortal(_logger);
		portal.Activated += OnPortalActivated;
		_portal = portal;

		// Fire-and-forget: the app is perfectly usable while the desktop decides, and on a desktop
		// with no GlobalShortcuts backend TryBindAsync just reports that and returns false.
		_linuxRegistration = Task.Run(async () =>
		{
			var shortcut = new PortalShortcut(
				NewWorkEntryShortcutId,
				_localization.GetString("AddNewWorkEntry"),
				NewWorkEntryTrigger);

			if (await portal.TryBindAsync([shortcut]).ConfigureAwait(false))
			{
				_isRegistered = true;
				_logger.LogInformation("Registered global shortcut {Trigger} through the desktop portal", NewWorkEntryTrigger);
			}
		});
	}

	private void UnregisterLinux()
	{
		var portal = _portal;
		var registration = _linuxRegistration;

		_portal = null;
		_linuxRegistration = null;
		_isRegistered = false;

		if (portal == null)
		{
			return;
		}

		portal.Activated -= OnPortalActivated;

		try
		{
			// Unregister runs on the shutdown path with no dispatcher pumping, so blocking here is
			// safe; the bind is bounded by its own timeouts and cannot hold shutdown indefinitely.
			registration?.GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Binding the global shortcut did not finish before shutdown");
		}

		try
		{
			portal.DisposeAsync().AsTask().GetAwaiter().GetResult();
			_logger.LogInformation("Released the global shortcut");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to release the global shortcut");
		}
	}

	private void OnPortalActivated(string shortcutId)
	{
		if (shortcutId == NewWorkEntryShortcutId)
		{
			HotkeyPressed?.Invoke(this, EventArgs.Empty);
		}
	}

	#endregion Linux implementation

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
