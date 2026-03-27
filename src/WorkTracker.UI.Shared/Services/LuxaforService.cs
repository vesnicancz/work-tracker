using HidSharp;
using Microsoft.Extensions.Logging;

namespace WorkTracker.UI.Shared.Services;

public sealed class LuxaforService : ILuxaforService
{
	private const int VendorId = 0x04D8;
	private const int ProductId = 0xF372;
	private const byte CommandStaticColor = 0x01;
	private const byte LedAll = 0xFF;

	private readonly ILogger<LuxaforService> _logger;
	private HidStream? _stream;
	private bool _disposed;

	public LuxaforService(ILogger<LuxaforService> logger)
	{
		_logger = logger;
	}

	public bool IsDeviceConnected => _stream != null && _stream.CanWrite;

	public void SetColor(byte r, byte g, byte b)
	{
		if (_disposed) return;

		try
		{
			var stream = GetOrOpenStream();
			if (stream == null) return;

			byte[] report = [0x00, CommandStaticColor, LedAll, r, g, b, 0x00, 0x00, 0x00];
			stream.Write(report);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to set Luxafor color");
			CloseStream();
		}
	}

	public void TurnOff()
	{
		SetColor(0, 0, 0);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		CloseStream();
	}

	private HidStream? GetOrOpenStream()
	{
		if (_stream != null && _stream.CanWrite)
			return _stream;

		CloseStream();

		try
		{
			var device = DeviceList.Local.GetHidDevices(VendorId, ProductId).FirstOrDefault();
			if (device == null)
			{
				_logger.LogDebug("Luxafor device not found");
				return null;
			}

			if (!device.TryOpen(out _stream))
			{
				_logger.LogWarning("Failed to open Luxafor HID stream");
				_stream = null;
				return null;
			}

			_logger.LogInformation("Luxafor device connected");
			return _stream;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to connect to Luxafor device");
			return null;
		}
	}

	private void CloseStream()
	{
		_stream?.Dispose();
		_stream = null;
	}
}
