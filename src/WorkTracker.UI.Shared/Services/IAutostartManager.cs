namespace WorkTracker.UI.Shared.Services;

public interface IAutostartManager
{
	bool IsEnabled { get; }

	void SetAutostart(bool enable);
}
