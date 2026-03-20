namespace WorkTracker.Application.Services;

public class DateRangeService : IDateRangeService
{
	public (DateTime start, DateTime end) GetWeekRange(DateTime date)
	{
		// Week starts on Monday
		var dayOfWeek = date.DayOfWeek;
		var daysFromMonday = dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;
		var monday = date.Date.AddDays(-daysFromMonday);
		var sunday = monday.AddDays(6);

		return (monday, sunday);
	}

	public (DateTime start, DateTime end) GetMonthRange(DateTime date)
	{
		var firstDay = new DateTime(date.Year, date.Month, 1);
		var lastDay = firstDay.AddMonths(1).AddDays(-1);

		return (firstDay, lastDay);
	}

	public IEnumerable<DateTime> GetDatesInRange(DateTime start, DateTime end)
	{
		for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
		{
			yield return date;
		}
	}
}