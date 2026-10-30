using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace WorkTracker.Avalonia.Services.MacOS;

/// <summary>
/// One system-wide hotkey registered through Carbon's <c>RegisterEventHotKey</c>.
/// <para>
/// Carbon is old, but this corner of it is still how macOS apps get a global shortcut: it needs no
/// Accessibility or Input Monitoring permission (unlike a CGEventTap or an NSEvent global monitor,
/// which watch every key the user types), it swallows the key combination instead of letting it
/// through to the focused app, and it works the same on Apple Silicon. The handler is installed on
/// the application event target, so the press arrives on the main thread through the run loop
/// Avalonia is already pumping.
/// </para>
/// <para>
/// Register and dispose on the main thread. Every failure is logged and reported through the return
/// value, never thrown: the app works without the shortcut.
/// </para>
/// </summary>
internal sealed class CarbonHotKey : IDisposable
{
	private const string CarbonLibrary = "/System/Library/Frameworks/Carbon.framework/Carbon";

	// Key code of the W key in the ANSI layout (kVK_ANSI_W). Key codes name a physical key, so this
	// is the key in the W position whatever the keyboard layout.
	internal const uint KeyCodeW = 0x0D;

	// Carbon modifier masks (Events.h).
	internal const uint ShiftKey = 0x0200;
	internal const uint ControlKey = 0x1000;

	private const uint KEventClassKeyboard = 0x6B657962;  // 'keyb'
	private const uint KEventHotKeyPressed = 5;
	private const uint KEventParamDirectObject = 0x2D2D2D2D;  // '----'
	private const uint TypeEventHotKeyID = 0x686B6964;  // 'hkid'
	private const int NoErr = 0;
	private const int EventNotHandledErr = -9874;

	// Our signature in the hotkey id, so a press meant for another handler on the same target is
	// passed on rather than claimed.
	private const uint Signature = 0x5754726B;  // 'WTrk'

	[StructLayout(LayoutKind.Sequential)]
	private struct EventTypeSpec
	{
		public uint EventClass;
		public uint EventKind;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct EventHotKeyId
	{
		public uint Signature;
		public uint Id;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int EventHandlerProc(IntPtr handlerCallRef, IntPtr eventRef, IntPtr userData);

	[DllImport(CarbonLibrary)]
	private static extern IntPtr GetApplicationEventTarget();

	[DllImport(CarbonLibrary)]
	private static extern int InstallEventHandler(
		IntPtr target, IntPtr handler, nuint numTypes, EventTypeSpec[] typeList, IntPtr userData, out IntPtr handlerRef);

	[DllImport(CarbonLibrary)]
	private static extern int RemoveEventHandler(IntPtr handlerRef);

	[DllImport(CarbonLibrary)]
	private static extern int RegisterEventHotKey(
		uint keyCode, uint modifiers, EventHotKeyId hotKeyId, IntPtr target, uint options, out IntPtr hotKeyRef);

	[DllImport(CarbonLibrary)]
	private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

	[DllImport(CarbonLibrary)]
	private static extern int GetEventParameter(
		IntPtr eventRef, uint name, uint desiredType, IntPtr actualType, nuint bufferSize, IntPtr actualSize, out EventHotKeyId data);

	private readonly ILogger _logger;
	private readonly uint _id;

	// Carbon keeps only the raw function pointer, so the delegate behind it has to outlive the
	// registration or the next press calls into collected memory.
	private readonly EventHandlerProc _handler;

	private IntPtr _handlerRef;
	private IntPtr _hotKeyRef;

	public CarbonHotKey(ILogger logger, uint id)
	{
		_logger = logger;
		_id = id;
		_handler = OnEvent;
	}

	/// <summary>Raised on the main thread when the registered combination is pressed.</summary>
	public event Action? Pressed;

	/// <summary>Registers <paramref name="keyCode"/> with <paramref name="modifiers"/>; false if macOS refused.</summary>
	public bool TryRegister(uint keyCode, uint modifiers)
	{
		try
		{
			var target = GetApplicationEventTarget();
			var types = new[] { new EventTypeSpec { EventClass = KEventClassKeyboard, EventKind = KEventHotKeyPressed } };

			var status = InstallEventHandler(
				target, Marshal.GetFunctionPointerForDelegate(_handler), (nuint)types.Length, types, IntPtr.Zero, out _handlerRef);
			if (status != NoErr)
			{
				_logger.LogWarning("Could not install the Carbon hotkey handler (OSStatus {Status})", status);
				return false;
			}

			var hotKeyId = new EventHotKeyId { Signature = Signature, Id = _id };
			status = RegisterEventHotKey(keyCode, modifiers, hotKeyId, target, 0, out _hotKeyRef);
			if (status != NoErr)
			{
				// eventHotKeyExistsErr (-9878) when another app already holds the combination.
				_logger.LogWarning("Could not register the global hotkey (OSStatus {Status})", status);
				Release();
				return false;
			}

			return true;
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			_logger.LogWarning(ex, "Carbon hotkey API is not available");
			Release();
			return false;
		}
	}

	public void Dispose()
	{
		Release();
	}

	private void Release()
	{
		if (_hotKeyRef != IntPtr.Zero)
		{
			UnregisterEventHotKey(_hotKeyRef);
			_hotKeyRef = IntPtr.Zero;
		}

		if (_handlerRef != IntPtr.Zero)
		{
			RemoveEventHandler(_handlerRef);
			_handlerRef = IntPtr.Zero;
		}
	}

	private int OnEvent(IntPtr handlerCallRef, IntPtr eventRef, IntPtr userData)
	{
		// An exception must not unwind into Carbon's native frames, which would take the process down.
		try
		{
			var status = GetEventParameter(
				eventRef, KEventParamDirectObject, TypeEventHotKeyID, IntPtr.Zero,
				(nuint)Marshal.SizeOf<EventHotKeyId>(), IntPtr.Zero, out var hotKeyId);

			if (status != NoErr || hotKeyId.Signature != Signature || hotKeyId.Id != _id)
			{
				return EventNotHandledErr;
			}

			Pressed?.Invoke();
			return NoErr;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Global hotkey handler failed");
			return NoErr;
		}
	}
}
