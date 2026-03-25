namespace WorkTracker.UI.Shared.Helpers;

public static class DurationFormatter
{
	public static string Format(int seconds)
	{
		var timeSpan = TimeSpan.FromSeconds(seconds);
		var hours = (int)timeSpan.TotalHours;
		var minutes = timeSpan.Minutes;

		if (hours > 0)
		{
			return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
		}

		return $"{minutes}m";
	}
}
