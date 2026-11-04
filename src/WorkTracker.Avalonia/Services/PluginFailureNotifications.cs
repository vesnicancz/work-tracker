using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Avalonia.Services;

public static class PluginFailureNotifications
{
	/// <summary>
	/// Tells the user which plugins could not be started. Without it they would only find out when
	/// the plugin's feature silently does nothing or fails later.
	/// </summary>
	public static void ReportPluginFailures(this INotificationService notifications, ILocalizationService localization, IReadOnlyList<PluginMetadata> failed)
	{
		if (failed.Count != 0)
		{
			notifications.ShowError(localization.GetFormattedString("PluginInitializationFailed", string.Join(", ", failed.Select(p => p.Name))));
		}
	}
}
