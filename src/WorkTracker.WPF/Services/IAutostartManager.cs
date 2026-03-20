namespace WorkTracker.WPF.Services;

public interface IAutostartManager
{
	bool IsEnabled { get; }

	void SetAutostart(bool enable);
}