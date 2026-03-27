using Luxafor.HidSharp;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Luxafor;

public sealed class LuxaforStatusIndicatorPlugin : StatusIndicatorPluginBase, IDisposable
{
	private static class ConfigKeys
	{
		public const string WorkColor = "work_color";
		public const string ShortBreakColor = "short_break_color";
		public const string LongBreakColor = "long_break_color";
	}

	private LuxaforDevice? _device;
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

	public override bool IsDeviceAvailable => _device is { IsConnected: true };

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return
		[
			new()
			{
				Key = ConfigKeys.WorkColor,
				Label = "Work color",
				Description = "Hex color for work phase (e.g. #FF0000)",
				DefaultValue = "#FF0000",
				Placeholder = "#FF0000",
				ValidationPattern = @"^#[0-9A-Fa-f]{6}$",
				ValidationMessage = "Must be a hex color (e.g. #FF0000)"
			},
			new()
			{
				Key = ConfigKeys.ShortBreakColor,
				Label = "Short break color",
				Description = "Hex color for short break phase (e.g. #00FF00)",
				DefaultValue = "#00FF00",
				Placeholder = "#00FF00",
				ValidationPattern = @"^#[0-9A-Fa-f]{6}$",
				ValidationMessage = "Must be a hex color (e.g. #00FF00)"
			},
			new()
			{
				Key = ConfigKeys.LongBreakColor,
				Label = "Long break color",
				Description = "Hex color for long break phase (e.g. #0000FF)",
				DefaultValue = "#0000FF",
				Placeholder = "#0000FF",
				ValidationPattern = @"^#[0-9A-Fa-f]{6}$",
				ValidationMessage = "Must be a hex color (e.g. #0000FF)"
			}
		];
	}

	public override Task SetStateAsync(StatusIndicatorState state, CancellationToken cancellationToken)
	{
		if (_disposed)
		{
			return Task.CompletedTask;
		}

		try
		{
			var device = GetOrOpenDevice();
			if (device == null)
			{
				return Task.CompletedTask;
			}

			switch (state)
			{
				case StatusIndicatorState.Work:
					device.SetColor(_workColor);
					break;
				case StatusIndicatorState.ShortBreak:
					device.SetColor(_shortBreakColor);
					break;
				case StatusIndicatorState.LongBreak:
					device.SetColor(_longBreakColor);
					break;
				default:
					device.TurnOff();
					break;
			}
		}
		catch (Exception ex)
		{
			Logger?.LogWarning(ex, "Failed to set Luxafor state to {State}", state);
			CloseDevice();
		}

		return Task.CompletedTask;
	}

	protected override Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		_workColor = ParseColor(GetConfigValue(ConfigKeys.WorkColor), LuxaforColor.Red);
		_shortBreakColor = ParseColor(GetConfigValue(ConfigKeys.ShortBreakColor), LuxaforColor.Green);
		_longBreakColor = ParseColor(GetConfigValue(ConfigKeys.LongBreakColor), LuxaforColor.Blue);

		return Task.FromResult(true);
	}

	protected override Task OnShutdownAsync()
	{
		try
		{
			_device?.TurnOff();
		}
		catch
		{
			// Best effort
		}

		CloseDevice();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		CloseDevice();
	}

	private LuxaforDevice? GetOrOpenDevice()
	{
		if (_device is { IsConnected: true })
		{
			return _device;
		}

		CloseDevice();

		try
		{
			_device = LuxaforDevice.TryOpen();
			if (_device == null)
			{
				Logger?.LogDebug("Luxafor device not found");
				return null;
			}

			Logger?.LogInformation("Luxafor device connected");
			return _device;
		}
		catch (Exception ex)
		{
			Logger?.LogWarning(ex, "Failed to connect to Luxafor device");
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
