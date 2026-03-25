namespace WorkTracker.Application.Services;

/// <summary>
/// Service for calculating date ranges
/// </summary>
public interface IDateRangeService
{
	/// <summary>
	/// Gets the start and end date of the week containing the specified date
	/// </summary>
	(DateTime start, DateTime end) GetWeekRange(DateTime date);

	/// <summary>
	/// Gets the start and end date of the month containing the specified date
	/// </summary>
	(DateTime start, DateTime end) GetMonthRange(DateTime date);

	/// <summary>
	/// Gets all dates in a range (inclusive)
	/// </summary>
	IEnumerable<DateTime> GetDatesInRange(DateTime start, DateTime end);
}
