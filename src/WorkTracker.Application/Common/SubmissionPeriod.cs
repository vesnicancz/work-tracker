namespace WorkTracker.Application.Common;

/// <summary>
/// The days worklogs are previewed or submitted for: one day, or the week around a date
/// </summary>
public readonly record struct SubmissionPeriod
{
	private SubmissionPeriod(DateTime date, bool isWeek)
	{
		Date = date.Date;
		IsWeek = isWeek;
	}

	public DateTime Date { get; }

	public bool IsWeek { get; }

	public static SubmissionPeriod Day(DateTime date) => new(date, isWeek: false);

	public static SubmissionPeriod WeekOf(DateTime date) => new(date, isWeek: true);
}
