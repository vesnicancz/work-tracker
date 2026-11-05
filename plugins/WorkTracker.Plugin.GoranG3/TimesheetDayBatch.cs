using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.GoranG3;

/// <summary>
/// Uploads worklogs day by day and closes a day once nothing of what was sent for it failed. Each day
/// stands on its own, so a failed entry keeps only its own day open.
/// <para>
/// Entries already on the server are remembered until their day closes. Closing can fail after every
/// entry went through, and retrying then sends one of those entries again; without this it would be
/// created twice just to get the day closed. Nothing is kept once a day closes, or across restarts.
/// </para>
/// </summary>
internal sealed class TimesheetDayBatch
{
	private readonly HashSet<EntryKey> _uploadedToOpenDays = [];

	public async Task<PluginResult<WorklogSubmissionResult>> RunAsync(
		IEnumerable<PluginWorklogEntry> worklogs,
		Func<IReadOnlyList<PluginWorklogEntry>, Task<PluginResult<WorklogSubmissionResult>>> uploadDay,
		Func<DateTime, Task<PluginResult<bool>>> closeDay)
	{
		var totalSuccessful = 0;
		var totalFailed = 0;
		var allErrors = new List<WorklogSubmissionError>();

		var positioned = worklogs.Select((worklog, index) => (Worklog: worklog, Index: index));

		foreach (var day in positioned.GroupBy(w => w.Worklog.StartTime.Date).OrderBy(g => g.Key))
		{
			var toUpload = day.Where(w => !_uploadedToOpenDays.Contains(EntryKey.Of(w.Worklog))).ToList();
			var alreadyUploaded = day.Count() - toUpload.Count;

			var dayResult = await uploadDay(toUpload.Select(w => w.Worklog).ToList());
			if (dayResult.IsFailure)
			{
				return dayResult;
			}

			var upload = dayResult.Value!;
			totalSuccessful += upload.SuccessfulEntries + alreadyUploaded;
			totalFailed += upload.FailedEntries;

			// The day's upload counts positions in that day, the caller in everything it handed over
			allErrors.AddRange(upload.Errors.Select(e => new WorklogSubmissionError
			{
				WorklogIndex = toUpload[e.WorklogIndex].Index,
				ErrorMessage = e.ErrorMessage
			}));

			var failed = upload.Errors.Select(e => e.WorklogIndex).ToHashSet();
			_uploadedToOpenDays.UnionWith(toUpload.Where((_, position) => !failed.Contains(position)).Select(w => EntryKey.Of(w.Worklog)));

			// Closing is final, so an incomplete day waits until a retry has uploaded the rest of it
			if (upload.FailedEntries > 0)
			{
				continue;
			}

			var closeResult = await closeDay(day.Key);
			if (closeResult.IsFailure)
			{
				allErrors.Add(new WorklogSubmissionError
				{
					WorklogIndex = day.First().Index,
					ErrorMessage = $"Timesheet submit failed for {day.Key:yyyy-MM-dd}: {closeResult.Error}"
				});
				continue;
			}

			_uploadedToOpenDays.RemoveWhere(key => key.StartTime.Date == day.Key);
		}

		return PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult
		{
			TotalEntries = totalSuccessful + totalFailed,
			SuccessfulEntries = totalSuccessful,
			FailedEntries = totalFailed,
			Errors = allErrors
		});
	}

	/// <summary>
	/// The values that reach the server. An entry edited since its upload counts as a new one.
	/// </summary>
	private readonly record struct EntryKey(DateTime StartTime, int DurationMinutes, string? TicketId, string? Description)
	{
		public static EntryKey Of(PluginWorklogEntry worklog) =>
			new(worklog.StartTime, worklog.DurationMinutes, worklog.TicketId, worklog.Description);
	}
}
