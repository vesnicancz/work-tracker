namespace Luxafor.HidSharp;

/// <summary>
/// Status information received from a Luxafor Bluetooth dongle,
/// including battery level and signal strength of the connected Bluetooth device.
/// </summary>
/// <param name="IsDevicePresent">Whether the Bluetooth device is in range of the dongle.</param>
/// <param name="Rssi">Received Signal Strength Indicator (dBm). Negative values are normal; closer to 0 = stronger signal.</param>
/// <param name="BatteryLevel">Battery charge level (0-100%).</param>
/// <param name="BatteryStatus">Current charging status.</param>
public readonly record struct DongleInfo(bool IsDevicePresent, int Rssi, int BatteryLevel, BatteryStatus BatteryStatus);
