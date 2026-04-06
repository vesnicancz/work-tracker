using System;

namespace Luxafor.HidSharp;

/// <summary>
/// Defines the public interface of a Luxafor LED device.
/// Supports color commands, animations, monitoring, and lifecycle management.
/// </summary>
public interface ILuxaforDevice : IDisposable
{
	/// <inheritdoc cref="LuxaforDevice.IsConnected"/>
	bool IsConnected { get; }
	/// <inheritdoc cref="LuxaforDevice.IsMonitoring"/>
	bool IsMonitoring { get; }
	/// <inheritdoc cref="LuxaforDevice.DeviceInfo"/>
	DeviceInfo? DeviceInfo { get; }

	/// <inheritdoc cref="LuxaforDevice.DeviceIdentified"/>
	event EventHandler<DeviceInfo>? DeviceIdentified;
	/// <inheritdoc cref="LuxaforDevice.DongleDataReceived"/>
	event EventHandler<DongleInfo>? DongleDataReceived;
	/// <inheritdoc cref="LuxaforDevice.MuteButtonStateChanged"/>
	event EventHandler<bool>? MuteButtonStateChanged;
	/// <inheritdoc cref="LuxaforDevice.PatternCompleted"/>
	event EventHandler? PatternCompleted;
	/// <inheritdoc cref="LuxaforDevice.Disconnected"/>
	event EventHandler? Disconnected;
	/// <inheritdoc cref="LuxaforDevice.ReadError"/>
	event EventHandler<Exception>? ReadError;

	/// <inheritdoc cref="LuxaforDevice.SetColor(byte, byte, byte)"/>
	void SetColor(byte r, byte g, byte b);
	/// <inheritdoc cref="LuxaforDevice.SetColor(LuxaforColor)"/>
	void SetColor(LuxaforColor color);
	/// <inheritdoc cref="LuxaforDevice.SetColor(Led, byte, byte, byte)"/>
	void SetColor(Led target, byte r, byte g, byte b);
	/// <inheritdoc cref="LuxaforDevice.SetColor(Led, LuxaforColor)"/>
	void SetColor(Led target, LuxaforColor color);

	/// <inheritdoc cref="LuxaforDevice.FadeTo(byte, byte, byte, byte)"/>
	void FadeTo(byte r, byte g, byte b, byte speed);
	/// <inheritdoc cref="LuxaforDevice.FadeTo(LuxaforColor, byte)"/>
	void FadeTo(LuxaforColor color, byte speed);
	/// <inheritdoc cref="LuxaforDevice.FadeTo(Led, byte, byte, byte, byte)"/>
	void FadeTo(Led target, byte r, byte g, byte b, byte speed);
	/// <inheritdoc cref="LuxaforDevice.FadeTo(Led, LuxaforColor, byte)"/>
	void FadeTo(Led target, LuxaforColor color, byte speed);

	/// <inheritdoc cref="LuxaforDevice.Strobe(byte, byte, byte, byte, byte)"/>
	void Strobe(byte r, byte g, byte b, byte speed, byte repeat);
	/// <inheritdoc cref="LuxaforDevice.Strobe(LuxaforColor, byte, byte)"/>
	void Strobe(LuxaforColor color, byte speed, byte repeat);
	/// <inheritdoc cref="LuxaforDevice.Strobe(Led, byte, byte, byte, byte, byte)"/>
	void Strobe(Led target, byte r, byte g, byte b, byte speed, byte repeat);
	/// <inheritdoc cref="LuxaforDevice.Strobe(Led, LuxaforColor, byte, byte)"/>
	void Strobe(Led target, LuxaforColor color, byte speed, byte repeat);

	/// <inheritdoc cref="LuxaforDevice.Wave(WaveType, byte, byte, byte, byte, byte)"/>
	void Wave(WaveType type, byte r, byte g, byte b, byte speed, byte repeat);
	/// <inheritdoc cref="LuxaforDevice.Wave(WaveType, LuxaforColor, byte, byte)"/>
	void Wave(WaveType type, LuxaforColor color, byte speed, byte repeat);

	/// <inheritdoc cref="LuxaforDevice.PlayPattern"/>
	void PlayPattern(BuiltInPattern pattern, byte repeat);
	/// <inheritdoc cref="LuxaforDevice.TurnOff"/>
	void TurnOff();

	/// <inheritdoc cref="LuxaforDevice.StartMonitoring"/>
	void StartMonitoring();
	/// <inheritdoc cref="LuxaforDevice.StopMonitoring"/>
	void StopMonitoring();
	/// <inheritdoc cref="LuxaforDevice.RequestDeviceInfo"/>
	void RequestDeviceInfo();
}
