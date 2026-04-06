namespace Luxafor.HidSharp;

/// <summary>
/// Default factory that opens real Luxafor HID devices.
/// </summary>
public sealed class LuxaforDeviceFactory : ILuxaforDeviceFactory
{
	/// <inheritdoc />
	public ILuxaforDevice? TryOpen() => LuxaforDevice.TryOpen();
}
