namespace Luxafor.HidSharp;

/// <summary>
/// Device identification information received from a Luxafor device after requesting its serial number.
/// </summary>
/// <param name="Type">The detected device type.</param>
/// <param name="SerialNumber">The device's serial number.</param>
public readonly record struct DeviceInfo(DeviceType Type, long SerialNumber);
