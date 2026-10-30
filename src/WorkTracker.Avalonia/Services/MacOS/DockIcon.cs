using System.Runtime.InteropServices;

namespace WorkTracker.Avalonia.Services.MacOS;

/// <summary>
/// Shows or hides the app's Dock icon by switching the activation policy of <c>NSApplication</c>.
/// <para>
/// On macOS the Dock icon belongs to the application, not to a window, so hiding the main window to
/// the tray leaves it there. The accessory policy is what menu bar apps run with: no Dock icon, no
/// entry in the Command-Tab switcher, but windows still open and take focus, so a dialog brought up
/// by the global hotkey works the same. Avalonia only offers the choice once, at startup
/// (<c>MacOSPlatformOptions.ShowInDock</c>), hence the direct call into the Objective-C runtime.
/// </para>
/// <para>
/// Call on the main thread. A no-op on other platforms; a failure leaves the icon as it was.
/// </para>
/// </summary>
internal static class DockIcon
{
	private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

	// NSApplicationActivationPolicy (NSRunningApplication.h).
	private const long ActivationPolicyRegular = 0;
	private const long ActivationPolicyAccessory = 1;

	[DllImport(ObjCLibrary)]
	private static extern IntPtr objc_getClass(string name);

	[DllImport(ObjCLibrary)]
	private static extern IntPtr sel_registerName(string name);

	// objc_msgSend has to be bound once per signature: on arm64 it is called with the target
	// method's own calling convention, not as a variadic function.
	[DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
	private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

	[DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool SendBoolLong(IntPtr receiver, IntPtr selector, long argument);

	public static void SetVisible(bool visible)
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		try
		{
			var application = SendIntPtr(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
			if (application == IntPtr.Zero)
			{
				return;
			}

			SendBoolLong(
				application,
				sel_registerName("setActivationPolicy:"),
				visible ? ActivationPolicyRegular : ActivationPolicyAccessory);
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			// The app works with the icon left in the Dock.
		}
	}
}
