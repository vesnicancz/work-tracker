using DotLuxafor;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Luxafor;

public sealed class LuxaforStatusIndicatorPlugin(ILogger<LuxaforStatusIndicatorPlugin> logger, ILuxaforDeviceManager? deviceManager = null)
	: StatusIndicatorPluginBase(logger), ITestablePlugin
{
	/// <summary>
	/// How long the connection test lights the device up. Long enough to be seen on the desk,
	/// short enough that the dialog does not feel stuck.
	/// </summary>
	private static readonly TimeSpan TestFlashDuration = TimeSpan.FromSeconds(1);

	private static class ConfigKeys
	{
		public const string WorkColor = "work_color";
		public const string ShortBreakColor = "short_break_color";
		public const string LongBreakColor = "long_break_color";
		public const string TurnOffOnStartup = "turn_off_on_startup";
	}

	private readonly ILuxaforDeviceManager _deviceManager = deviceManager ?? new LuxaforDeviceManager();
	private readonly SemaphoreSlim _deviceLock = new(1, 1);
	private ILuxaforDevice? _device;
	private bool _disposed;

	private StatusIndicatorState _lastState = StatusIndicatorState.Idle;

	private LuxaforColor _workColor = LuxaforColor.Red;
	private LuxaforColor _shortBreakColor = LuxaforColor.Green;
	private LuxaforColor _longBreakColor = LuxaforColor.Blue;

	public override PluginMetadata Metadata => new()
	{
		Id = "luxafor.status-indicator",
		Name = "Luxafor LED",
		Version = new Version(1, 1, 0),
		Author = "WorkTracker Team",
		Description = "Show current Pomodoro phase on Luxafor Bluetooth Pro LED indicator",
		Tags = ["luxafor", "led", "status-indicator"]
	};

	public override bool IsDeviceAvailable
	{
		get
		{
			var device = _device;
			return device is { IsConnected: true };
		}
	}

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return
		[
			HexColorField(ConfigKeys.WorkColor, "Work color", "#FF0000"),
			HexColorField(ConfigKeys.ShortBreakColor, "Short break color", "#00FF00"),
			HexColorField(ConfigKeys.LongBreakColor, "Long break color", "#0000FF"),
			new PluginConfigurationField
			{
				Key = ConfigKeys.TurnOffOnStartup,
				Label = "Turn off on startup",
				Description = "Turn off the Luxafor device when the application starts",
				Type = PluginConfigurationFieldType.Checkbox,
				DefaultValue = "false"
			}
		];
	}

	private static PluginConfigurationField HexColorField(string key, string label, string defaultHex) => new()
	{
		Key = key,
		Label = label,
		Description = $"Hex color for {label.ToLowerInvariant()} (e.g. {defaultHex})",
		DefaultValue = defaultHex,
		Placeholder = defaultHex,
		ValidationPattern = @"^#[0-9A-Fa-f]{6}$",
		ValidationMessage = "Must be a hex color (e.g. #FF0000)"
	};

	public override async Task SetStateAsync(StatusIndicatorState state, CancellationToken cancellationToken)
	{
		if (_disposed)
		{
			return;
		}

		await _deviceLock.WaitAsync(cancellationToken);
		try
		{
			_lastState = state;

			await RunWithReopenAsync(device => ApplyStateAsync(device, state, cancellationToken));
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (LuxaforDeviceDisconnectedException ex)
		{
			// Gone again right after the reopen, so the device really is not there. Expected on a
			// wireless device, hence no warning - the next call tries again from scratch.
			Logger.LogDebug(ex, "Luxafor device went away while setting state {State}", state);
			CloseDevice();
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Failed to set Luxafor state to {State}", state);
			CloseDevice();
		}
		finally
		{
			_deviceLock.Release();
		}
	}

	public async Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		if (_disposed)
		{
			return PluginResult<bool>.Failure("The Luxafor plugin has been shut down.");
		}

		await _deviceLock.WaitAsync(cancellationToken);
		try
		{
			progress?.Report("Looking for a Luxafor device…");

			var device = _device is { IsConnected: true } ? _device : null;
			if (device == null)
			{
				var result = OpenDevice();
				if (result == null)
				{
					return PluginResult<bool>.Failure("Could not reach the Luxafor device.");
				}

				if (!result.IsSuccess)
				{
					return PluginResult<bool>.Failure(result.Description, CategorizeOpenFailure(result.Status));
				}

				device = result.Device!;
			}

			progress?.Report($"Found {DescribeDevice(device)} — lighting it up.");

			var flashed = await RunWithReopenAsync(async d =>
			{
				await d.SetColorAsync(_workColor, cancellationToken: cancellationToken);
				await Task.Delay(TestFlashDuration, cancellationToken);

				// Put the LED back on the phase the timer is in, so testing from the settings
				// dialog does not leave a running Pomodoro showing the wrong color.
				await ApplyStateAsync(d, _lastState, cancellationToken);
			});

			return flashed == null
				? PluginResult<bool>.Failure("The Luxafor device is no longer connected.", PluginErrorCategory.NotFound)
				: PluginResult<bool>.Success(true);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (LuxaforDeviceDisconnectedException ex)
		{
			CloseDevice();
			return PluginResult<bool>.Failure(ex.Message, PluginErrorCategory.NotFound);
		}
		catch (Exception ex)
		{
			CloseDevice();
			return PluginResult<bool>.Failure($"Connection failed: {ex.Message}");
		}
		finally
		{
			_deviceLock.Release();
		}
	}

	protected override async Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		_workColor = ParseColor(GetConfigValue(ConfigKeys.WorkColor), LuxaforColor.Red);
		_shortBreakColor = ParseColor(GetConfigValue(ConfigKeys.ShortBreakColor), LuxaforColor.Green);
		_longBreakColor = ParseColor(GetConfigValue(ConfigKeys.LongBreakColor), LuxaforColor.Blue);

		if (!IsInitialized && bool.TryParse(GetConfigValue(ConfigKeys.TurnOffOnStartup), out var turnOff) && turnOff)
		{
			await SetStateAsync(StatusIndicatorState.Idle, cancellationToken);
		}

		return true;
	}

	protected override async Task OnShutdownAsync()
	{
		if (_disposed)
		{
			return;
		}

		await _deviceLock.WaitAsync();
		try
		{
			if (_device != null)
			{
				try
				{
					await _device.TurnOffAsync();
				}
				catch (OperationCanceledException) { /* Expected during shutdown */ }
				catch (Exception ex)
				{
					Logger.LogWarning(ex, "Failed to turn off Luxafor device during shutdown");
				}
			}
		}
		finally
		{
			_deviceLock.Release();
		}
	}

	protected override async ValueTask OnDisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		await _deviceLock.WaitAsync();
		try
		{
			CloseDevice();
		}
		finally
		{
			_deviceLock.Release();
		}
	}

	private async Task ApplyStateAsync(ILuxaforDevice device, StatusIndicatorState state, CancellationToken cancellationToken)
	{
		switch (state)
		{
			case StatusIndicatorState.Work:
				await device.SetColorAsync(_workColor, cancellationToken: cancellationToken);
				break;
			case StatusIndicatorState.ShortBreak:
				await device.SetColorAsync(_shortBreakColor, cancellationToken: cancellationToken);
				break;
			case StatusIndicatorState.LongBreak:
				await device.SetColorAsync(_longBreakColor, cancellationToken: cancellationToken);
				break;
			default:
				await device.TurnOffAsync(cancellationToken);
				break;
		}
	}

	/// <summary>
	/// Runs a command against the open device, reopening once and repeating the command when the
	/// device turns out to have gone away.
	/// </summary>
	/// <remarks>
	/// <see cref="ILuxaforConnection.IsConnected"/> cannot carry this: it reports whether the
	/// handle was closed, not whether the hardware is still attached, so an unplugged device is
	/// first noticed by the command itself throwing. Without the repeat the phase would never
	/// reach the LED and the next one is a Pomodoro away.
	/// </remarks>
	/// <returns>The device the command ran against, or <c>null</c> when there was none to run it on.</returns>
	private async Task<ILuxaforDevice?> RunWithReopenAsync(Func<ILuxaforDevice, Task> command)
	{
		var device = GetOrOpenDevice();
		if (device == null)
		{
			return null;
		}

		try
		{
			await command(device);
			return device;
		}
		catch (LuxaforDeviceDisconnectedException ex)
		{
			Logger.LogDebug(ex, "Luxafor device went away mid-command; reopening once");
			CloseDevice();
			device = ReopenDevice(ex.Descriptor);
		}

		if (device == null)
		{
			return null;
		}

		// Once. A second failure means the device really is gone, and the exception says so to
		// the caller.
		await command(device);
		return device;
	}

	/// <summary>
	/// Reopens the device that went away, preferring the one the exception named and falling back
	/// to whatever is attached - a device replugged into another port comes back on a new path.
	/// </summary>
	/// <remarks>
	/// The fallback is only safe while this plugin drives a single device: with two Luxafors
	/// attached it can open the other one. Drop it if the plugin ever lets the user pick a device.
	/// </remarks>
	private ILuxaforDevice? ReopenDevice(LuxaforDeviceDescriptor? descriptor)
	{
		if (descriptor != null)
		{
			var reopened = OpenDevice(() => _deviceManager.Open(descriptor));
			if (reopened is { IsSuccess: true })
			{
				return reopened.Device;
			}
		}

		return OpenDevice()?.Device;
	}

	private ILuxaforDevice? GetOrOpenDevice()
	{
		if (_device is { IsConnected: true })
		{
			return _device;
		}

		return OpenDevice()?.Device;
	}

	private DeviceOpenResult? OpenDevice() => OpenDevice(_deviceManager.Open);

	/// <summary>
	/// Opens a device the given way and logs the outcome, keeping the opened one in
	/// <see cref="_device"/>. Returns <c>null</c> when the manager threw instead of reporting an
	/// outcome, which is the one thing <see cref="ILuxaforDeviceOpener.Open"/> does not describe.
	/// </summary>
	private DeviceOpenResult? OpenDevice(Func<DeviceOpenResult> open)
	{
		CloseDevice();

		DeviceOpenResult outcome;
		try
		{
			outcome = open();
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Failed to connect to Luxafor device");
			return null;
		}

		switch (outcome.Status)
		{
			case DeviceOpenStatus.Opened:
				_device = outcome.Device;
				Logger.LogInformation("Luxafor device connected: {Device}", DescribeDevice(outcome.Device));
				break;
			case DeviceOpenStatus.NotFound:
				Logger.LogDebug("Luxafor device not found");
				break;
			default:
				// A device is attached but unusable - a missing udev rule on Linux, Input
				// Monitoring on macOS, another application holding it open. Worth a warning:
				// the LED simply staying dark is otherwise the only symptom.
				Logger.LogWarning(outcome.Error, "Luxafor device unavailable: {Reason}", outcome.Description);
				break;
		}

		return outcome;
	}

	private static PluginErrorCategory CategorizeOpenFailure(DeviceOpenStatus status) => status switch
	{
		DeviceOpenStatus.NotFound => PluginErrorCategory.NotFound,
		DeviceOpenStatus.AccessDenied => PluginErrorCategory.Authentication,
		_ => PluginErrorCategory.Internal
	};

	private static string DescribeDevice(ILuxaforDevice? device)
		=> device?.Descriptor?.ToString() ?? "a Luxafor device";

	private void CloseDevice()
	{
		_device?.Dispose();
		_device = null;
	}

	private static LuxaforColor ParseColor(string? hex, LuxaforColor fallback)
	{
		return LuxaforColor.TryFromHex(hex, out var color) ? color : fallback;
	}
}
