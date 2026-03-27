# Luxafor.HidSharp

.NET library for controlling [Luxafor](https://luxafor.com/) LED devices via HID using [HidSharp](https://github.com/IntergatedCircuits/HidSharp).

Supports **Luxafor Flag**, **Bluetooth Pro** (via USB dongle), **Mute Button**, **Smart Button**, and **Colorblind** devices.

## Features

- **Commands**: Static color, fade, strobe, wave, and built-in patterns
- **Per-LED targeting**: Individual LEDs, top/bottom side, or all
- **Input report monitoring**: Battery level, mute button state, pattern completion
- **Device identification**: Auto-detect device type and serial number
- **Multi-device support**: Control multiple Luxafor devices simultaneously
- **Predefined colors** and hex color parsing

## Quick Start

```csharp
using Luxafor.HidSharp;

using var device = LuxaforDevice.TryOpen();
if (device == null)
{
    Console.WriteLine("No Luxafor device found.");
    return;
}

// Solid color
device.SetColor(LuxaforColor.Red);

// Fade
device.FadeTo(LuxaforColor.Green, speed: 20);

// Strobe
device.Strobe(LuxaforColor.Blue, speed: 10, repeat: 5);

// Wave
device.Wave(WaveType.Smooth, LuxaforColor.Cyan, speed: 10, repeat: 3);

// Built-in pattern
device.Pattern(BuiltInPattern.Rainbow, repeat: 3);

// Per-LED control
device.SetColor(Led.TopSide, LuxaforColor.Red);
device.SetColor(Led.BottomSide, LuxaforColor.Green);

// Hex color
device.SetColor(LuxaforColor.FromHex("#FF8800"));

// Turn off
device.TurnOff();
```

## Battery & Device Monitoring

For Bluetooth Pro devices (via dongle), you can monitor battery level, signal strength, and device presence:

```csharp
using var device = LuxaforDevice.TryOpen();

device.DongleDataReceived += (sender, info) =>
{
    Console.WriteLine($"Battery: {info.BatteryLevel}%");
    Console.WriteLine($"Status: {info.BatteryStatus}");
    Console.WriteLine($"RSSI: {info.Rssi} dBm");
    Console.WriteLine($"Device present: {info.IsDevicePresent}");
};

device.DeviceIdentified += (sender, info) =>
{
    Console.WriteLine($"Device type: {info.Type}");
    Console.WriteLine($"Serial: {info.SerialNumber}");
};

device.PatternCompleted += (sender, _) =>
{
    Console.WriteLine("Pattern finished playing.");
};

// Start listening for input reports
device.StartMonitoring();

// Request device identification
device.RequestDeviceInfo();
```

## Mute Button

```csharp
device.MuteButtonStateChanged += (sender, isPressed) =>
{
    Console.WriteLine(isPressed ? "Muted" : "Unmuted");
};

device.StartMonitoring();
```

## API Reference

### LuxaforDevice

| Method | Description |
|--------|-------------|
| `TryOpen()` | Opens the first connected device, or returns `null` |
| `OpenAll()` | Opens all connected devices |
| `IsDevicePresent()` | Checks if any device is connected (without opening) |
| `SetColor(...)` | Sets LED(s) to a solid color |
| `FadeTo(...)` | Smoothly transitions LED(s) to a color |
| `Strobe(...)` | Flashes LED(s) with a color |
| `Wave(...)` | Plays a wave animation |
| `Pattern(...)` | Plays a built-in hardware pattern |
| `TurnOff()` | Turns off all LEDs |
| `StartMonitoring()` | Starts background input report reading |
| `StopMonitoring()` | Stops input report reading |
| `RequestDeviceInfo()` | Requests device type and serial number |

### Events

| Event | Description |
|-------|-------------|
| `DongleDataReceived` | Battery level, RSSI, device presence (Bluetooth Pro) |
| `MuteButtonStateChanged` | Mute button pressed/released |
| `PatternCompleted` | Built-in pattern finished playing |
| `DeviceIdentified` | Device type and serial number received |
| `ReadError` | Error during input report reading |

### Enums

| Enum | Values |
|------|--------|
| `Led` | `All`, `TopSide`, `BottomSide`, `Led1`-`Led6` |
| `WaveType` | `Short`, `Long`, `ShortOverlapping`, `LongOverlapping`, `Smooth` |
| `BuiltInPattern` | `TrafficLights`, `Random1`-`Random5`, `Police`, `Rainbow` |
| `DeviceType` | `Standard`, `Bluetooth`, `MuteButton`, `SmartButton`, `Colorblind` |
| `BatteryStatus` | `NotConnected`, `Charging`, `Full` |

## Supported Devices

| Device | Commands | Battery | Mute Button |
|--------|----------|---------|-------------|
| Luxafor Flag | Yes | No | No |
| Luxafor Bluetooth Pro | Yes | Yes (via dongle) | No |
| Luxafor Mute Button | Yes | Yes (via dongle) | Yes |
| Luxafor Smart Button | Yes | Yes (via dongle) | No |

## License

MIT
