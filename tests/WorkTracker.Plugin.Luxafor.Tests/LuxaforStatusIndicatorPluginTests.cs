using FluentAssertions;
using Luxafor.HidSharp;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Plugin.Luxafor;

namespace WorkTracker.Plugin.Luxafor.Tests;

public class LuxaforStatusIndicatorPluginTests : IAsyncDisposable
{
	private readonly LuxaforStatusIndicatorPlugin _plugin;

	private static readonly Dictionary<string, string> ValidConfig = new()
	{
		["work_color"] = "#FF0000",
		["short_break_color"] = "#00FF00",
		["long_break_color"] = "#0000FF"
	};

	public LuxaforStatusIndicatorPluginTests()
	{
		_plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceFactory: new MockLuxaforDeviceFactory(() => null));
	}

	public async ValueTask DisposeAsync()
	{
		await _plugin.DisposeAsync();
	}

	private async Task InitializePluginAsync(IDictionary<string, string>? config = null)
	{
		var initialized = await _plugin.InitializeAsync(config ?? ValidConfig, TestContext.Current.CancellationToken);
		initialized.Should().BeTrue("plugin initialization should succeed");
	}

	private static LuxaforStatusIndicatorPlugin CreatePluginWithMockDevice(MockLuxaforDevice device)
	{
		return new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceFactory: new MockLuxaforDeviceFactory(() => device));
	}

	#region Metadata

	[Fact]
	public void Metadata_HasCorrectId()
	{
		_plugin.Metadata.Id.Should().Be("luxafor.status-indicator");
	}

	[Fact]
	public void Metadata_HasCorrectNameAndVersion()
	{
		_plugin.Metadata.Name.Should().Be("Luxafor LED");
		_plugin.Metadata.Version.Should().Be(new Version(1, 0, 0));
	}

	#endregion

	#region Configuration Fields

	[Fact]
	public void GetConfigurationFields_ReturnsThreeHexColorFields()
	{
		var fields = _plugin.GetConfigurationFields();

		fields.Should().HaveCount(3);
		fields.Select(f => f.Key).Should().BeEquivalentTo(["work_color", "short_break_color", "long_break_color"]);
	}

	[Fact]
	public void GetConfigurationFields_AllFieldsHaveHexValidationPattern()
	{
		var fields = _plugin.GetConfigurationFields();

		foreach (var field in fields)
		{
			field.ValidationPattern.Should().Be(@"^#[0-9A-Fa-f]{6}$");
		}
	}

	[Fact]
	public void GetConfigurationFields_FieldsHaveCorrectDefaults()
	{
		var fields = _plugin.GetConfigurationFields();

		fields.Single(f => f.Key == "work_color").DefaultValue.Should().Be("#FF0000");
		fields.Single(f => f.Key == "short_break_color").DefaultValue.Should().Be("#00FF00");
		fields.Single(f => f.Key == "long_break_color").DefaultValue.Should().Be("#0000FF");
	}

	#endregion

	#region Configuration Validation

	[Fact]
	public async Task ValidateConfigurationAsync_ValidHexColors_Succeeds()
	{
		var result = await _plugin.ValidateConfigurationAsync(ValidConfig, TestContext.Current.CancellationToken);

		result.IsValid.Should().BeTrue();
	}

	[Fact]
	public async Task ValidateConfigurationAsync_EmptyConfig_Succeeds()
	{
		var result = await _plugin.ValidateConfigurationAsync(new Dictionary<string, string>(), TestContext.Current.CancellationToken);

		result.IsValid.Should().BeTrue();
	}

	#endregion

	#region Initialization

	[Fact]
	public async Task InitializeAsync_ValidColors_ReturnsTrue()
	{
		var result = await _plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		result.Should().BeTrue();
	}

	[Fact]
	public async Task InitializeAsync_EmptyConfig_ReturnsTrue()
	{
		var result = await _plugin.InitializeAsync(new Dictionary<string, string>(), TestContext.Current.CancellationToken);

		result.Should().BeTrue();
	}

	[Fact]
	public async Task InitializeAsync_InvalidHexColor_StillSucceeds_UsesFallback()
	{
		var config = new Dictionary<string, string>
		{
			["work_color"] = "invalid",
			["short_break_color"] = "not-a-color",
			["long_break_color"] = "#ZZZ"
		};

		var result = await _plugin.InitializeAsync(config, TestContext.Current.CancellationToken);

		result.Should().BeTrue();
	}

	#endregion

	#region Device Availability

	[Fact]
	public async Task IsDeviceAvailable_NoDevice_ReturnsFalse()
	{
		await InitializePluginAsync();

		_plugin.IsDeviceAvailable.Should().BeFalse();
	}

	[Fact]
	public async Task IsDeviceAvailable_ConnectedDevice_ReturnsTrue()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);

		plugin.IsDeviceAvailable.Should().BeTrue();
	}

	#endregion

	#region SetStateAsync — No Device

	[Theory]
	[InlineData(StatusIndicatorState.Idle)]
	[InlineData(StatusIndicatorState.Work)]
	[InlineData(StatusIndicatorState.ShortBreak)]
	[InlineData(StatusIndicatorState.LongBreak)]
	public async Task SetStateAsync_AllStates_NoDevice_CompletesWithoutError(StatusIndicatorState state)
	{
		await InitializePluginAsync();

		var act = () => _plugin.SetStateAsync(state, TestContext.Current.CancellationToken);

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task SetStateAsync_AfterDispose_ReturnsWithoutError()
	{
		await InitializePluginAsync();
		await _plugin.DisposeAsync();

		var act = () => _plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);

		await act.Should().NotThrowAsync();
	}

	#endregion

	#region SetStateAsync — With Mock Device

	[Theory]
	[InlineData(StatusIndicatorState.Work, "#FF0000")]
	[InlineData(StatusIndicatorState.ShortBreak, "#00FF00")]
	[InlineData(StatusIndicatorState.LongBreak, "#0000FF")]
	public async Task SetStateAsync_SetsCorrectColor(StatusIndicatorState state, string expectedHex)
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(state, TestContext.Current.CancellationToken);

		var expectedColor = LuxaforColor.FromHex(expectedHex);
		device.LastColor.Should().Be(expectedColor);
	}

	[Fact]
	public async Task SetStateAsync_Idle_TurnsOff()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(StatusIndicatorState.Idle, TestContext.Current.CancellationToken);

		device.TurnedOff.Should().BeTrue();
	}

	[Fact]
	public async Task SetStateAsync_CustomHexColors_UsesConfiguredColors()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		var config = new Dictionary<string, string>
		{
			["work_color"] = "#AABBCC",
			["short_break_color"] = "#112233",
			["long_break_color"] = "#DDEEFF"
		};
		await plugin.InitializeAsync(config, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);
		device.LastColor.Should().Be(new LuxaforColor(0xAA, 0xBB, 0xCC));

		await plugin.SetStateAsync(StatusIndicatorState.ShortBreak, TestContext.Current.CancellationToken);
		device.LastColor.Should().Be(new LuxaforColor(0x11, 0x22, 0x33));

		await plugin.SetStateAsync(StatusIndicatorState.LongBreak, TestContext.Current.CancellationToken);
		device.LastColor.Should().Be(new LuxaforColor(0xDD, 0xEE, 0xFF));
	}

	[Fact]
	public async Task SetStateAsync_DeviceThrows_ClosesDeviceAndRecreatesOnNextCall()
	{
		var failingDevice = new MockLuxaforDevice { ThrowOnSetColor = true };
		var newDevice = new MockLuxaforDevice();
		var callCount = 0;
		await using var plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceFactory: new MockLuxaforDeviceFactory(() => callCount++ == 0 ? failingDevice : newDevice));
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);
		failingDevice.Disposed.Should().BeTrue();

		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);
		newDevice.LastColor.Should().Be(LuxaforColor.Red);
	}

	[Fact]
	public async Task SetStateAsync_ReusesConnectedDevice()
	{
		var factoryCallCount = 0;
		var device = new MockLuxaforDevice();
		await using var plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceFactory: new MockLuxaforDeviceFactory(() => { factoryCallCount++; return device; }));
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);
		await plugin.SetStateAsync(StatusIndicatorState.ShortBreak, TestContext.Current.CancellationToken);

		factoryCallCount.Should().Be(1);
		device.SetColorCallCount.Should().Be(2);
	}

	#endregion

	#region Lifecycle

	[Fact]
	public async Task DisposeAsync_CanBeCalledMultipleTimes()
	{
		await _plugin.DisposeAsync();
		await _plugin.DisposeAsync();
	}

	[Fact]
	public async Task ShutdownAsync_NoDevice_CompletesWithoutError()
	{
		await InitializePluginAsync();

		var act = () => _plugin.ShutdownAsync();

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task ShutdownAsync_TurnsOffDevice()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);
		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);

		await plugin.ShutdownAsync();

		device.TurnedOff.Should().BeTrue();
	}

	#endregion
}

internal sealed class MockLuxaforDeviceFactory(Func<ILuxaforDevice?> factory) : ILuxaforDeviceFactory
{
	public ILuxaforDevice? TryOpen() => factory();
}

#pragma warning disable CS0067
internal sealed class MockLuxaforDevice : ILuxaforDevice
{
	public bool IsConnected { get; set; } = true;
	public bool IsMonitoring => false;
	public DeviceInfo? DeviceInfo => null;

	public LuxaforColor? LastColor { get; private set; }
	public bool TurnedOff { get; private set; }
	public int SetColorCallCount { get; private set; }
	public bool Disposed { get; private set; }
	public bool ThrowOnSetColor { get; set; }

	public void SetColor(LuxaforColor color)
	{
		if (ThrowOnSetColor)
		{
			throw new InvalidOperationException("Device error");
		}

		LastColor = color;
		SetColorCallCount++;
	}

	public void SetColor(byte r, byte g, byte b) => SetColor(new LuxaforColor(r, g, b));
	public void SetColor(Led target, byte r, byte g, byte b) => SetColor(r, g, b);
	public void SetColor(Led target, LuxaforColor color) => SetColor(color);

	public void TurnOff() => TurnedOff = true;

	public void FadeTo(byte r, byte g, byte b, byte speed) { }
	public void FadeTo(LuxaforColor color, byte speed) { }
	public void FadeTo(Led target, byte r, byte g, byte b, byte speed) { }
	public void FadeTo(Led target, LuxaforColor color, byte speed) { }

	public void Strobe(byte r, byte g, byte b, byte speed, byte repeat) { }
	public void Strobe(LuxaforColor color, byte speed, byte repeat) { }
	public void Strobe(Led target, byte r, byte g, byte b, byte speed, byte repeat) { }
	public void Strobe(Led target, LuxaforColor color, byte speed, byte repeat) { }

	public void Wave(WaveType type, byte r, byte g, byte b, byte speed, byte repeat) { }
	public void Wave(WaveType type, LuxaforColor color, byte speed, byte repeat) { }

	public void PlayPattern(BuiltInPattern pattern, byte repeat) { }

	public void StartMonitoring() { }
	public void StopMonitoring() { }
	public void RequestDeviceInfo() { }

	public event EventHandler<DeviceInfo>? DeviceIdentified;
	public event EventHandler<DongleInfo>? DongleDataReceived;
	public event EventHandler<bool>? MuteButtonStateChanged;
	public event EventHandler? PatternCompleted;
	public event EventHandler? Disconnected;
	public event EventHandler<Exception>? ReadError;

	public void Dispose()
	{
		Disposed = true;
		IsConnected = false;
	}
}
#pragma warning restore CS0067
