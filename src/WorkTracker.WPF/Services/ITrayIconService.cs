namespace WorkTracker.WPF.Services;

public interface ITrayIconService
{
	void Initialize();

	void Show();

	void Hide();

	/// <summary>
	/// Refreshes the favorites submenu with current settings
	/// </summary>
	void RefreshFavoritesMenu();

	void Dispose();
}