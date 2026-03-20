using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WorkTracker.WPF.Services;

/// <summary>
/// Service for managing global hotkeys using Windows API
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
	private const int WM_HOTKEY = 0x0312;
	private const int HOTKEY_ID = 9000;

	// Modifier keys
	private const uint MOD_ALT = 0x0001;
	private const uint MOD_CONTROL = 0x0002;
	private const uint MOD_SHIFT = 0x0004;
	private const uint MOD_WIN = 0x0008;

	// Virtual key code for 'W'
	private const uint VK_W = 0x57;

	private HwndSource? _source;
	private bool _isRegistered;

	public event EventHandler? HotkeyPressed;

	[DllImport("user32.dll")]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	public void Register()
	{
		if (_isRegistered)
		{
			return;
		}

		var windowHandle = GetMainWindowHandle();
		if (windowHandle == IntPtr.Zero)
		{
			return;
		}

		// Register Ctrl+Alt+W hotkey
		bool success = RegisterHotKey(windowHandle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_W);

		if (success)
		{
			_source = HwndSource.FromHwnd(windowHandle);
			if (_source != null)
			{
				_source.AddHook(HwndHook);
				_isRegistered = true;
			}
		}
	}

	public void Unregister()
	{
		if (!_isRegistered)
		{
			return;
		}

		var windowHandle = GetMainWindowHandle();
		if (windowHandle != IntPtr.Zero)
		{
			UnregisterHotKey(windowHandle, HOTKEY_ID);
		}

		if (_source != null)
		{
			_source.RemoveHook(HwndHook);
			_source = null;
		}

		_isRegistered = false;
	}

	public void Dispose()
	{
		Unregister();
	}

	private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
		{
			HotkeyPressed?.Invoke(this, EventArgs.Empty);
			handled = true;
		}

		return IntPtr.Zero;
	}

	private static IntPtr GetMainWindowHandle()
	{
		var mainWindow = System.Windows.Application.Current?.MainWindow;
		if (mainWindow == null)
		{
			return IntPtr.Zero;
		}

		var helper = new WindowInteropHelper(mainWindow);
		return helper.Handle;
	}
}
