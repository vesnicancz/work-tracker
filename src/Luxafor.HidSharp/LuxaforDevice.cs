using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HidSharp;

namespace Luxafor.HidSharp;

/// <summary>
/// Controls a Luxafor LED device (Flag, Bluetooth Pro, Mute Button, Smart Button) via HID.
/// Supports sending commands (color, fade, strobe, wave, pattern) and receiving input reports
/// (battery status, mute button state, pattern completion, device identification).
/// </summary>
/// <remarks>
/// All command methods are thread-safe and can be called while monitoring is active.
/// Events are raised from a background thread — marshalling to the UI thread is the caller's responsibility.
/// </remarks>
public sealed class LuxaforDevice : IDisposable
{
	/// <summary>Luxafor USB Vendor ID (Microchip).</summary>
	public const int VendorId = 0x04D8;

	/// <summary>Luxafor USB Product ID.</summary>
	public const int ProductId = 0xF372;

	private const byte CommandStaticColor = 0x01;
	private const byte CommandFade = 0x02;
	private const byte CommandStrobe = 0x03;
	private const byte CommandWave = 0x04;
	private const byte CommandPattern = 0x06;

	private const byte ReportDeviceInfo = 0x80;
	private const byte ReportMuteButton = 0x83;
	private const byte ReportDongle = 0x41;

	private const int ReportMinLength = 9;
	private const int MonitoringReadTimeoutMs = 500;

	private readonly object _streamLock = new object();
	private HidStream? _stream;
	private Thread? _readThread;
	private volatile bool _disposed;
	private volatile bool _monitoring;

	private LuxaforDevice(HidStream stream)
	{
		_stream = stream;
	}

	/// <summary>
	/// Gets whether the device connection is still active.
	/// </summary>
	public bool IsConnected
	{
		get
		{
			lock (_streamLock)
			{
				return !_disposed && _stream is { CanWrite: true };
			}
		}
	}

	/// <summary>
	/// Gets whether input report monitoring is currently active.
	/// </summary>
	public bool IsMonitoring => _monitoring;

	/// <summary>
	/// Gets the device identification info, available after <see cref="DeviceIdentified"/> fires.
	/// </summary>
	public DeviceInfo? DeviceInfo { get; private set; }

	/// <summary>
	/// Raised when a device identification report is received (after calling <see cref="RequestDeviceInfo"/>).
	/// Contains the device type and serial number.
	/// </summary>
	public event EventHandler<DeviceInfo>? DeviceIdentified;

	/// <summary>
	/// Raised when a dongle status report is received (Bluetooth Pro devices only).
	/// Contains battery level, charging status, RSSI, and device presence.
	/// Reports are sent periodically by the dongle while monitoring is active.
	/// </summary>
	public event EventHandler<DongleInfo>? DongleDataReceived;

	/// <summary>
	/// Raised when a mute button state change is detected (Mute Button devices only).
	/// The boolean value indicates whether the button is currently pressed (<c>true</c>) or released (<c>false</c>).
	/// </summary>
	public event EventHandler<bool>? MuteButtonStateChanged;

	/// <summary>
	/// Raised when the device finishes playing a pattern or wave animation.
	/// </summary>
	public event EventHandler? PatternCompleted;

	/// <summary>
	/// Raised when the device connection is lost (stream becomes unreadable or unwritable).
	/// Monitoring stops automatically after this event.
	/// </summary>
	public event EventHandler? Disconnected;

	/// <summary>
	/// Raised when an error occurs during input report reading.
	/// Monitoring continues after the error unless the device is disconnected.
	/// </summary>
	public event EventHandler<Exception>? ReadError;

	#region Factory Methods

	/// <summary>
	/// Tries to find and open the first connected Luxafor device.
	/// Returns <c>null</c> if no device is found or cannot be opened.
	/// </summary>
	public static LuxaforDevice? TryOpen()
	{
		var device = DeviceList.Local.GetHidDevices(VendorId, ProductId).FirstOrDefault();
		if (device == null)
		{
			return null;
		}

		if (!device.TryOpen(out var stream))
		{
			return null;
		}

		return new LuxaforDevice(stream);
	}

	/// <summary>
	/// Opens all connected Luxafor devices.
	/// </summary>
	public static IReadOnlyList<LuxaforDevice> OpenAll()
	{
		var devices = new List<LuxaforDevice>();
		foreach (var hidDevice in DeviceList.Local.GetHidDevices(VendorId, ProductId))
		{
			if (hidDevice.TryOpen(out var stream))
			{
				devices.Add(new LuxaforDevice(stream));
			}
		}
		return devices;
	}

	/// <summary>
	/// Gets whether any Luxafor device is currently connected (without opening it).
	/// </summary>
	public static bool IsDevicePresent()
	{
		return DeviceList.Local.GetHidDevices(VendorId, ProductId).Any();
	}

	#endregion

	#region Monitoring

	/// <summary>
	/// Starts a background thread that reads input reports from the device.
	/// Input reports include battery status, mute button state, pattern completion, and device identification.
	/// Subscribe to events before calling this method.
	/// </summary>
	/// <exception cref="InvalidOperationException">Monitoring is already active.</exception>
	public void StartMonitoring()
	{
		ThrowIfDisposed();

		if (_monitoring)
		{
			throw new InvalidOperationException("Monitoring is already active.");
		}

		lock (_streamLock)
		{
			if (_stream == null)
			{
				throw new InvalidOperationException("Device is not connected.");
			}

			_stream.ReadTimeout = MonitoringReadTimeoutMs;
		}

		_monitoring = true;
		_readThread = new Thread(ReadLoop)
		{
			IsBackground = true,
			Name = "Luxafor-InputReport-Reader"
		};
		_readThread.Start();
	}

	/// <summary>
	/// Stops the background input report reading thread and waits for it to finish.
	/// </summary>
	public void StopMonitoring()
	{
		_monitoring = false;
		_readThread?.Join();
		_readThread = null;
	}

	/// <summary>
	/// Sends a request to the device to identify itself.
	/// The response will arrive as an input report and trigger the <see cref="DeviceIdentified"/> event.
	/// <see cref="StartMonitoring"/> must be called first to receive the response.
	/// </summary>
	public void RequestDeviceInfo()
	{
		ThrowIfDisposed();

		lock (_streamLock)
		{
			var stream = _stream ?? throw new InvalidOperationException("Device is not connected.");
			byte[] request = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
			stream.SetFeature(request);
		}
	}

	#endregion

	#region Static Color

	/// <summary>
	/// Sets all LEDs to a solid color.
	/// </summary>
	public void SetColor(byte r, byte g, byte b)
		=> SetColor(Led.All, r, g, b);

	/// <summary>
	/// Sets all LEDs to a solid color.
	/// </summary>
	public void SetColor(LuxaforColor color)
		=> SetColor(Led.All, color.R, color.G, color.B);

	/// <summary>
	/// Sets the specified LED(s) to a solid color.
	/// </summary>
	public void SetColor(Led target, byte r, byte g, byte b)
		=> SendReport(CommandStaticColor, (byte)target, r, g, b, 0x00, 0x00);

	/// <summary>
	/// Sets the specified LED(s) to a solid color.
	/// </summary>
	public void SetColor(Led target, LuxaforColor color)
		=> SetColor(target, color.R, color.G, color.B);

	#endregion

	#region Fade

	/// <summary>
	/// Fades all LEDs to a color at the given speed.
	/// </summary>
	/// <param name="r">Red component (0-255).</param>
	/// <param name="g">Green component (0-255).</param>
	/// <param name="b">Blue component (0-255).</param>
	/// <param name="speed">Fade speed (0 = instant, 255 = slowest).</param>
	public void FadeTo(byte r, byte g, byte b, byte speed)
		=> FadeTo(Led.All, r, g, b, speed);

	/// <summary>
	/// Fades all LEDs to a color at the given speed.
	/// </summary>
	public void FadeTo(LuxaforColor color, byte speed)
		=> FadeTo(Led.All, color.R, color.G, color.B, speed);

	/// <summary>
	/// Fades the specified LED(s) to a color at the given speed.
	/// </summary>
	/// <param name="target">Which LED(s) to target.</param>
	/// <param name="r">Red component (0-255).</param>
	/// <param name="g">Green component (0-255).</param>
	/// <param name="b">Blue component (0-255).</param>
	/// <param name="speed">Fade speed (0 = instant, 255 = slowest).</param>
	public void FadeTo(Led target, byte r, byte g, byte b, byte speed)
		=> SendReport(CommandFade, (byte)target, r, g, b, speed, 0x00);

	/// <summary>
	/// Fades the specified LED(s) to a color at the given speed.
	/// </summary>
	public void FadeTo(Led target, LuxaforColor color, byte speed)
		=> FadeTo(target, color.R, color.G, color.B, speed);

	#endregion

	#region Strobe

	/// <summary>
	/// Strobes (flashes) all LEDs with the specified color.
	/// </summary>
	/// <param name="r">Red component (0-255).</param>
	/// <param name="g">Green component (0-255).</param>
	/// <param name="b">Blue component (0-255).</param>
	/// <param name="speed">Flash speed (0 = fastest, 255 = slowest).</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	public void Strobe(byte r, byte g, byte b, byte speed, byte repeat)
		=> Strobe(Led.All, r, g, b, speed, repeat);

	/// <summary>
	/// Strobes (flashes) all LEDs with the specified color.
	/// </summary>
	public void Strobe(LuxaforColor color, byte speed, byte repeat)
		=> Strobe(Led.All, color.R, color.G, color.B, speed, repeat);

	/// <summary>
	/// Strobes (flashes) the specified LED(s) with the specified color.
	/// </summary>
	/// <param name="target">Which LED(s) to target.</param>
	/// <param name="r">Red component (0-255).</param>
	/// <param name="g">Green component (0-255).</param>
	/// <param name="b">Blue component (0-255).</param>
	/// <param name="speed">Flash speed (0 = fastest, 255 = slowest).</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	public void Strobe(Led target, byte r, byte g, byte b, byte speed, byte repeat)
		=> SendReport(CommandStrobe, (byte)target, r, g, b, speed, repeat);

	/// <summary>
	/// Strobes (flashes) the specified LED(s) with the specified color.
	/// </summary>
	public void Strobe(Led target, LuxaforColor color, byte speed, byte repeat)
		=> Strobe(target, color.R, color.G, color.B, speed, repeat);

	#endregion

	#region Wave

	/// <summary>
	/// Plays a wave animation with the specified color.
	/// </summary>
	/// <param name="type">Wave animation type.</param>
	/// <param name="r">Red component (0-255).</param>
	/// <param name="g">Green component (0-255).</param>
	/// <param name="b">Blue component (0-255).</param>
	/// <param name="speed">Wave speed (0 = fastest, 255 = slowest).</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	public void Wave(WaveType type, byte r, byte g, byte b, byte speed, byte repeat)
		=> SendReport(CommandWave, (byte)type, r, g, b, repeat, speed);

	/// <summary>
	/// Plays a wave animation with the specified color.
	/// </summary>
	public void Wave(WaveType type, LuxaforColor color, byte speed, byte repeat)
		=> Wave(type, color.R, color.G, color.B, speed, repeat);

	#endregion

	#region Pattern

	/// <summary>
	/// Plays a built-in animation pattern on the device.
	/// Subscribe to <see cref="PatternCompleted"/> to know when the pattern finishes
	/// (requires <see cref="StartMonitoring"/> to be active).
	/// </summary>
	/// <param name="pattern">The pattern to play.</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	public void PlayPattern(BuiltInPattern pattern, byte repeat)
		=> SendReport(CommandPattern, (byte)pattern, repeat, 0x00, 0x00, 0x00, 0x00);

	#endregion

	/// <summary>
	/// Turns off all LEDs.
	/// </summary>
	public void TurnOff()
		=> SetColor(0, 0, 0);

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_monitoring = false;

		lock (_streamLock)
		{
			_stream?.Dispose();
			_stream = null;
		}

		_readThread?.Join(millisecondsTimeout: 2000);
		_readThread = null;
	}

	private void SendReport(byte command, byte param1, byte param2, byte param3, byte param4, byte param5, byte param6)
	{
		ThrowIfDisposed();

		lock (_streamLock)
		{
			if (_stream == null || !_stream.CanWrite)
			{
				throw new InvalidOperationException("Device is not connected.");
			}

			byte[] report = new byte[] { 0x00, command, param1, param2, param3, param4, param5, param6, 0x00 };
			_stream.Write(report);
		}
	}

	private void ReadLoop()
	{
		var buffer = new byte[ReportMinLength];

		while (_monitoring && !_disposed)
		{
			try
			{
				HidStream? stream;
				lock (_streamLock)
				{
					stream = _stream;
				}

				if (stream == null || !stream.CanRead)
				{
					_monitoring = false;
					Disconnected?.Invoke(this, EventArgs.Empty);
					break;
				}

				int bytesRead;
				try
				{
					bytesRead = stream.Read(buffer, 0, buffer.Length);
				}
				catch (TimeoutException)
				{
					// ReadTimeout elapsed — check loop condition and retry
					continue;
				}

				if (bytesRead < ReportMinLength)
				{
					continue;
				}

				ProcessReport(buffer);
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch (IOException)
			{
				_monitoring = false;
				Disconnected?.Invoke(this, EventArgs.Empty);
				break;
			}
			catch (Exception ex)
			{
				ReadError?.Invoke(this, ex);
			}
		}

		_monitoring = false;
	}

	private void ProcessReport(byte[] buffer)
	{
		// Dongle status report (battery, RSSI, device presence)
		if (buffer[1] == ReportDongle)
		{
			var devicePresent = Convert.ToBoolean(buffer[2]);
			var rssi = (int)(sbyte)buffer[3];
			var batteryLevel = Convert.ToInt32(buffer[6]);
			var batteryStatus = buffer[7] switch
			{
				1 => BatteryStatus.Charging,
				2 => BatteryStatus.Full,
				_ => BatteryStatus.NotConnected
			};

			DongleDataReceived?.Invoke(this, new DongleInfo(devicePresent, rssi, batteryLevel, batteryStatus));
			return;
		}

		// Mute button state report
		if (buffer[1] == ReportMuteButton)
		{
			var isPressed = buffer[2] == 1;
			MuteButtonStateChanged?.Invoke(this, isPressed);
			return;
		}

		// Device identification report (serial number + device type)
		if (buffer[1] == ReportDeviceInfo)
		{
			var deviceType = ClassifyDevice(buffer[2]);
			var serialNumber = ExtractSerialNumber(buffer, deviceType);
			var info = new DeviceInfo(deviceType, serialNumber);
			DeviceInfo = info;
			DeviceIdentified?.Invoke(this, info);
			return;
		}

		// Pattern completion report
		if ((buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 1) ||
		    (buffer[0] == 0 && buffer[1] == 1 && buffer[2] == 0))
		{
			PatternCompleted?.Invoke(this, EventArgs.Empty);
		}
	}

	private static DeviceType ClassifyDevice(byte typeByte) => typeByte switch
	{
		4 => DeviceType.Colorblind,
		30 => DeviceType.MuteButton,
		50 => DeviceType.SmartButton,
		>= 10 and < 30 => DeviceType.Bluetooth,
		_ => DeviceType.Standard
	};

	private static long ExtractSerialNumber(byte[] buffer, DeviceType type)
	{
		return type switch
		{
			DeviceType.Bluetooth or DeviceType.MuteButton or DeviceType.SmartButton =>
				((long)buffer[3] << 40) | ((long)buffer[4] << 32) |
				((long)buffer[5] << 24) | ((long)buffer[6] << 16) |
				((long)buffer[7] << 8) | buffer[8],

			_ => ((long)buffer[3] << 8) | buffer[4]
		};
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(GetType().FullName);
		}
	}
}
