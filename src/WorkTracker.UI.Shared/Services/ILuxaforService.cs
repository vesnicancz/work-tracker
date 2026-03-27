namespace WorkTracker.UI.Shared.Services;

public interface ILuxaforService : IDisposable
{
	bool IsDeviceConnected { get; }
	void SetColor(byte r, byte g, byte b);
	void TurnOff();
}
