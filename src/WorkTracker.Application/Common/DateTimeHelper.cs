namespace WorkTracker.Application.Common;

/// <summary>
/// Helper methods for DateTime operations
/// </summary>
public static class DateTimeHelper
{
	/// <summary>
	/// Rounds a DateTime to the nearest minute (removes seconds and milliseconds)
	/// </summary>
	public static DateTime RoundToMinute(DateTime dateTime)
	{
		return new DateTime(
			dateTime.Year,
			dateTime.Month,
			dateTime.Day,
			dateTime.Hour,
			dateTime.Minute,
			0,
			dateTime.Kind);
	}

	/// <summary>
	/// Rounds a nullable DateTime to the nearest minute (removes seconds and milliseconds)
	/// </summary>
	public static DateTime? RoundToMinute(DateTime? dateTime)
	{
		return dateTime.HasValue ? RoundToMinute(dateTime.Value) : null;
	}
}