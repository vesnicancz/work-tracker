namespace WorkTracker.Avalonia.Services;

public interface IAutostartManager
{
	bool IsEnabled { get; }

	void SetAutostart(bool enable);
}
