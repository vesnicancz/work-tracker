using DotLuxafor;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Luxafor;

public sealed class LuxaforStatusIndicatorPlugin(ILogger<LuxaforStatusIndicatorPlugin> logger, ILuxaforDeviceManager? deviceManager = null)
	: StatusIndicatorPluginBase(logger)
{
	private static class ConfigKeys
	{
		public const string WorkColor = "work_color";
		public const string ShortBreakColor = "short_break_color";
		public const string LongBreakColor = "long_break_color";
	}

	private readonly ILuxaforDeviceManager _deviceManager = deviceManager ?? new LuxaforDeviceManager();
	private readonly SemaphoreSlim _deviceLock = new(1, 1);
	private ILuxaforDevice? _device;
	private bool _disposed;

	private LuxaforColor _workColor = LuxaforColor.Red;
	private LuxaforColor _shortBreakColor = LuxaforColor.Green;
	private LuxaforColor _longBreakColor = LuxaforColor.Blue;

	public override PluginMetadata Metadata => new()
	{
		Id = "luxafor.status-indicator",
		Name = "Luxafor LED",
		Version = new Version(1, 0, 0),
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
			HexColorField(ConfigKeys.LongBreakColor, "Long break color", "#0000FF")
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
			var device = GetOrOpenDevice();
			if (device == null)
			{
				return;
			}

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
		catch (OperationCanceledException)
		{
			throw;
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

	protected override Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		_workColor = ParseColor(GetConfigValue(ConfigKeys.WorkColor), LuxaforColor.Red);
		_shortBreakColor = ParseColor(GetConfigValue(ConfigKeys.ShortBreakColor), LuxaforColor.Green);
		_longBreakColor = ParseColor(GetConfigValue(ConfigKeys.LongBreakColor), LuxaforColor.Blue);

		return Task.FromResult(true);
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
				catch (OperationCanceledException) { }
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

	private ILuxaforDevice? GetOrOpenDevice()
	{
		if (_device is { IsConnected: true })
		{
			return _device;
		}

		CloseDevice();

		try
		{
			_device = _deviceManager.TryOpen();
			if (_device == null)
			{
				Logger.LogDebug("Luxafor device not found");
				return null;
			}

			Logger.LogInformation("Luxafor device connected");
			return _device;
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Failed to connect to Luxafor device");
			return null;
		}
	}

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
