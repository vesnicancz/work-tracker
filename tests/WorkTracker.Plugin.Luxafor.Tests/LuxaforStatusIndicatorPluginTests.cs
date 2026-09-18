using DotLuxafor;
using FluentAssertions;
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
			deviceManager: new MockLuxaforDeviceManager(() => null));
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
			deviceManager: new MockLuxaforDeviceManager(() => device));
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
		_plugin.Metadata.Version.Should().Be(new Version(1, 1, 0));
	}

	#endregion

	#region Configuration Fields

	[Fact]
	public void GetConfigurationFields_ReturnsExpectedFields()
	{
		var fields = _plugin.GetConfigurationFields();

		fields.Should().HaveCount(4);
		fields.Select(f => f.Key).Should().BeEquivalentTo(["work_color", "short_break_color", "long_break_color", "turn_off_on_startup"]);
	}

	[Fact]
	public void GetConfigurationFields_HexColorFieldsHaveHexValidationPattern()
	{
		var fields = _plugin.GetConfigurationFields();

		foreach (var field in fields.Where(f => f.Key.EndsWith("_color")))
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
		fields.Single(f => f.Key == "turn_off_on_startup").DefaultValue.Should().Be("false");
	}

	[Fact]
	public void GetConfigurationFields_TurnOffOnStartup_IsCheckbox()
	{
		var field = _plugin.GetConfigurationFields().Single(f => f.Key == "turn_off_on_startup");

		field.Type.Should().Be(PluginConfigurationFieldType.Checkbox);
		field.IsRequired.Should().BeFalse();
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

	[Fact]
	public async Task InitializeAsync_TurnOffOnStartupTrue_TurnsOffDevice()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);

		var config = new Dictionary<string, string>(ValidConfig) { ["turn_off_on_startup"] = "true" };
		await plugin.InitializeAsync(config, TestContext.Current.CancellationToken);

		device.TurnedOff.Should().BeTrue();
	}

	[Fact]
	public async Task InitializeAsync_TurnOffOnStartupFalse_DoesNotTouchDevice()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);

		var config = new Dictionary<string, string>(ValidConfig) { ["turn_off_on_startup"] = "false" };
		await plugin.InitializeAsync(config, TestContext.Current.CancellationToken);

		device.TurnedOff.Should().BeFalse();
		device.LastColor.Should().BeNull();
	}

	[Fact]
	public async Task InitializeAsync_TurnOffOnStartupMissing_DoesNotTouchDevice()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);

		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		device.TurnedOff.Should().BeFalse();
		device.LastColor.Should().BeNull();
	}

	[Fact]
	public async Task InitializeAsync_TurnOffOnStartupTrue_OnReinitialize_DoesNotTurnOffAgain()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		var config = new Dictionary<string, string>(ValidConfig) { ["turn_off_on_startup"] = "true" };

		await plugin.InitializeAsync(config, TestContext.Current.CancellationToken);
		device.TurnOffCallCount.Should().Be(1);

		await plugin.InitializeAsync(config, TestContext.Current.CancellationToken);

		device.TurnOffCallCount.Should().Be(1, "re-initialization must not turn off a running indicator");
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
			deviceManager: new MockLuxaforDeviceManager(() => callCount++ == 0 ? failingDevice : newDevice));
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
			deviceManager: new MockLuxaforDeviceManager(() => { factoryCallCount++; return device; }));
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);
		await plugin.SetStateAsync(StatusIndicatorState.ShortBreak, TestContext.Current.CancellationToken);

		factoryCallCount.Should().Be(1);
		device.SetColorCallCount.Should().Be(2);
	}

	#endregion

	#region SetStateAsync — Disconnected Device

	[Fact]
	public async Task SetStateAsync_DeviceVanishesMidCommand_ReopensTheSameDeviceAndStillSetsTheColor()
	{
		var descriptor = new LuxaforDeviceDescriptor("/dev/luxafor-a", "Mock Luxafor", "SN-1");
		var goneDevice = new MockLuxaforDevice
		{
			Descriptor = descriptor,
			SetColorError = new LuxaforDeviceDisconnectedException(descriptor, new IOException("Device went away"))
		};
		var newDevice = new MockLuxaforDevice();
		var callCount = 0;
		var manager = new MockLuxaforDeviceManager(() => callCount++ == 0 ? goneDevice : newDevice);
		await using var plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceManager: manager);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);

		goneDevice.Disposed.Should().BeTrue();
		newDevice.LastColor.Should().Be(LuxaforColor.Red, "the phase must reach the LED now, not in 25 minutes");
		plugin.IsDeviceAvailable.Should().BeTrue();
		manager.TargetedPaths.Should()
			.ContainSingle("the reopen goes for the device that went away, not for whatever is attached")
			.Which.Should().Be("/dev/luxafor-a");
	}

	[Fact]
	public async Task SetStateAsync_DeviceVanishesAgainAfterReopen_GivesUpQuietly()
	{
		var manager = new MockLuxaforDeviceManager(
			() => new MockLuxaforDevice { SetColorError = new LuxaforDeviceDisconnectedException() });
		await using var plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceManager: manager);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		var act = () => plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);

		await act.Should().NotThrowAsync();
		manager.OpenCallCount.Should().Be(2, "one open and exactly one reopen - a second failure means the device really is gone");
		plugin.IsDeviceAvailable.Should().BeFalse();
	}

	[Fact]
	public async Task SetStateAsync_DeviceReturnsOnAnotherPath_FallsBackToWhateverIsAttached()
	{
		var goneDescriptor = new LuxaforDeviceDescriptor("/dev/gone", "Mock Luxafor", "SN-1");
		var goneDevice = new MockLuxaforDevice
		{
			Descriptor = goneDescriptor,
			SetColorError = new LuxaforDeviceDisconnectedException(goneDescriptor, new IOException("Unplugged"))
		};
		var newDevice = new MockLuxaforDevice { Descriptor = new LuxaforDeviceDescriptor("/dev/new", "Mock Luxafor", "SN-1") };
		var callCount = 0;
		var manager = new MockLuxaforDeviceManager(() => callCount++ == 0 ? goneDevice : newDevice);
		manager.DetachedPaths.Add("/dev/gone");
		await using var plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceManager: manager);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.SetStateAsync(StatusIndicatorState.Work, TestContext.Current.CancellationToken);

		manager.TargetedPaths.Should()
			.ContainSingle("the old path is tried first, and only then given up on")
			.Which.Should().Be("/dev/gone");
		newDevice.LastColor.Should().Be(LuxaforColor.Red);
	}

	#endregion

	#region TestConnectionAsync

	[Fact]
	public void Plugin_IsTestable()
	{
		_plugin.Should().BeAssignableTo<ITestablePlugin>();
	}

	[Fact]
	public async Task TestConnectionAsync_NoDevice_FailsAsNotFound()
	{
		await InitializePluginAsync();

		var result = await _plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.ErrorCategory.Should().Be(PluginErrorCategory.NotFound);
		result.Error.Should().Contain("No Luxafor device found");
	}

	[Fact]
	public async Task TestConnectionAsync_AccessDenied_ReportsTheReasonFromTheLibrary()
	{
		var manager = new MockLuxaforDeviceManager(() => null)
		{
			FailureResult = DeviceOpenResult.Failure(
				DeviceOpenStatus.AccessDenied,
				MockLuxaforDeviceManager.DefaultDescriptor,
				new UnauthorizedAccessException("Permission denied"))
		};
		await using var plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceManager: manager);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		var result = await plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.ErrorCategory.Should().Be(PluginErrorCategory.Authentication);
		result.Error.Should().Contain("denied access", "the reason from the library is the whole point of testing the connection");
	}

	[Fact]
	public async Task TestConnectionAsync_OpenerThrows_FailsWithoutThrowing()
	{
		var manager = new MockLuxaforDeviceManager(() => null) { ThrowOnOpen = new IOException("HID stack exploded") };
		await using var plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceManager: manager);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		var result = await plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
	}

	[Fact]
	public async Task TestConnectionAsync_ConnectedDevice_FlashesWorkColorAndReportsSuccess()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		var result = await plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		device.ColorHistory.Should().Contain(LuxaforColor.Red);
	}

	[Fact]
	public async Task TestConnectionAsync_DeviceVanishesMidFlash_ReopensAndSucceeds()
	{
		var goneDevice = new MockLuxaforDevice { SetColorError = new LuxaforDeviceDisconnectedException() };
		var newDevice = new MockLuxaforDevice();
		var callCount = 0;
		await using var plugin = new LuxaforStatusIndicatorPlugin(
			NullLogger<LuxaforStatusIndicatorPlugin>.Instance,
			deviceManager: new MockLuxaforDeviceManager(() => callCount++ == 0 ? goneDevice : newDevice));
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		var result = await plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		newDevice.ColorHistory.Should().Contain(LuxaforColor.Red);
	}

	[Fact]
	public async Task TestConnectionAsync_RestoresTheCurrentPhaseAfterwards()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);
		await plugin.SetStateAsync(StatusIndicatorState.ShortBreak, TestContext.Current.CancellationToken);

		await plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		device.LastColor.Should().Be(LuxaforColor.Green, "a test from the settings dialog must not leave a running Pomodoro on the wrong color");
	}

	[Fact]
	public async Task TestConnectionAsync_IdleTimer_TurnsTheDeviceBackOff()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		await plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		device.TurnedOff.Should().BeTrue();
	}

	[Fact]
	public async Task TestConnectionAsync_ReportsProgress()
	{
		var device = new MockLuxaforDevice();
		await using var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);
		var messages = new List<string>();

		await plugin.TestConnectionAsync(new Progress<string>(messages.Add), TestContext.Current.CancellationToken);

		messages.Should().NotBeEmpty();
	}

	[Fact]
	public async Task TestConnectionAsync_AfterDispose_FailsWithoutTouchingTheDevice()
	{
		var device = new MockLuxaforDevice();
		var plugin = CreatePluginWithMockDevice(device);
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);
		await plugin.DisposeAsync();

		var result = await plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		device.SetColorCallCount.Should().Be(0);
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

internal sealed class MockLuxaforDeviceManager(Func<ILuxaforDevice?> factory) : ILuxaforDeviceManager
{
	public static readonly LuxaforDeviceDescriptor DefaultDescriptor =
		new("/dev/mock-luxafor", "Mock Luxafor", "SN-1");

	/// <summary>The result to report when <paramref name="factory"/> hands back no device.</summary>
	public DeviceOpenResult FailureResult { get; set; } = DeviceOpenResult.NotFound();

	public Exception? ThrowOnOpen { get; set; }

	public int OpenCallCount { get; private set; }

	/// <summary>Device paths that are no longer attached, so a targeted reopen of them fails.</summary>
	public HashSet<string> DetachedPaths { get; } = [];

	/// <summary>Paths a targeted <see cref="Open(string)"/> asked for, in order.</summary>
	public List<string> TargetedPaths { get; } = [];

	public DeviceOpenResult Open()
	{
		OpenCallCount++;

		if (ThrowOnOpen != null)
		{
			throw ThrowOnOpen;
		}

		var device = factory();
		if (device == null)
		{
			return FailureResult;
		}

		// The real manager stamps the descriptor it discovered onto the device it opened.
		if (device is MockLuxaforDevice mock)
		{
			mock.Descriptor ??= DefaultDescriptor;
		}

		return DeviceOpenResult.Opened(device, device.Descriptor ?? DefaultDescriptor);
	}

	public DeviceOpenResult Open(string devicePath)
	{
		TargetedPaths.Add(devicePath);

		return DetachedPaths.Contains(devicePath) ? DeviceOpenResult.NotFound(devicePath) : Open();
	}

	public DeviceOpenResult Open(LuxaforDeviceDescriptor descriptor) => Open(descriptor.DevicePath);

	public ILuxaforDevice? TryOpen() => Open().Device;

	public IReadOnlyList<LuxaforDeviceDescriptor> List() => [];

	public IReadOnlyList<ILuxaforDevice> OpenAll() => [];

	public IReadOnlyList<DeviceOpenResult> OpenAllResults() => [];

	public bool IsDevicePresent() => false;

	public bool IsPresent(LuxaforDeviceDescriptor descriptor) => !DetachedPaths.Contains(descriptor.DevicePath);

	public Task WaitForDeviceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class MockLuxaforDevice : ILuxaforDevice
{
	public bool IsConnected { get; set; } = true;
	public DeviceInfo? DeviceInfo => null;
	public LuxaforDeviceDescriptor? Descriptor { get; set; }
	LuxaforColor? ILuxaforConnection.LastColor => LastColor;

	public LuxaforColor? LastColor { get; private set; }
	public bool TurnedOff { get; private set; }
	public int TurnOffCallCount { get; private set; }
	public int SetColorCallCount { get; private set; }
	public bool Disposed { get; private set; }
	public bool ThrowOnSetColor { get; set; }
	public Exception? SetColorError { get; set; }
	public List<LuxaforColor> ColorHistory { get; } = [];

	public Task SetColorAsync(LuxaforColor color, LedTarget target, CancellationToken cancellationToken)
	{
		if (SetColorError != null)
		{
			throw SetColorError;
		}

		if (ThrowOnSetColor)
		{
			throw new InvalidOperationException("Device error");
		}

		LastColor = color;
		ColorHistory.Add(color);
		SetColorCallCount++;
		return Task.CompletedTask;
	}

	public Task TurnOffAsync(CancellationToken cancellationToken)
	{
		TurnedOff = true;
		TurnOffCallCount++;
		return Task.CompletedTask;
	}

	public Task FadeToAsync(LuxaforColor color, byte speed, LedTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StrobeAsync(LuxaforColor color, byte speed, byte repeat, LedTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
	public Task WaveAsync(WaveType type, LuxaforColor color, byte speed, byte repeat, CancellationToken cancellationToken) => Task.CompletedTask;
	public Task PlayPatternAsync(BuiltInPattern pattern, byte repeat, CancellationToken cancellationToken) => Task.CompletedTask;

	public Task RequestDeviceInfoAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public IAsyncEnumerable<LuxaforEvent> ObserveAsync(CancellationToken cancellationToken) =>
		AsyncEnumerable.Empty<LuxaforEvent>();

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	public void Dispose()
	{
		Disposed = true;
		IsConnected = false;
	}
}
