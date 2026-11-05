using FluentAssertions;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.GoranG3.Tests;

public class TimesheetDayBatchTests
{
	private static readonly DateTime Monday = new(2026, 11, 2);
	private static readonly DateTime Tuesday = Monday.AddDays(1);

	private readonly TimesheetDayBatch _batch = new();
	private readonly List<PluginWorklogEntry> _uploaded = [];
	private readonly List<DateTime> _closedDays = [];
	private readonly HashSet<string> _failingTickets = [];
	private bool _closeFails;

	private static PluginWorklogEntry Entry(DateTime day, string ticket, int hour = 9) => new()
	{
		TicketId = ticket,
		StartTime = day.AddHours(hour),
		EndTime = day.AddHours(hour + 1),
		DurationMinutes = 60
	};

	private Task<PluginResult<WorklogSubmissionResult>> RunAsync(params PluginWorklogEntry[] worklogs) =>
		_batch.RunAsync(worklogs, UploadDay, CloseDay);

	private Task<PluginResult<WorklogSubmissionResult>> UploadDay(IReadOnlyList<PluginWorklogEntry> day)
	{
		var errors = new List<WorklogSubmissionError>();
		for (var index = 0; index < day.Count; index++)
		{
			if (_failingTickets.Contains(day[index].TicketId!))
			{
				errors.Add(new WorklogSubmissionError { WorklogIndex = index, ErrorMessage = "rejected" });
			}
			else
			{
				_uploaded.Add(day[index]);
			}
		}

		return Task.FromResult(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult
		{
			TotalEntries = day.Count,
			SuccessfulEntries = day.Count - errors.Count,
			FailedEntries = errors.Count,
			Errors = errors
		}));
	}

	private Task<PluginResult<bool>> CloseDay(DateTime date)
	{
		if (_closeFails)
		{
			return Task.FromResult(PluginResult<bool>.Failure("closed for maintenance"));
		}

		_closedDays.Add(date);
		return Task.FromResult(PluginResult<bool>.Success(true));
	}

	[Fact]
	public async Task EveryEntryUploaded_ClosesTheDay()
	{
		var result = await RunAsync(Entry(Monday, "A-1"), Entry(Monday, "A-2", hour: 10));

		_closedDays.Should().Equal(Monday);
		result.Value!.SuccessfulEntries.Should().Be(2);
		result.Value.Errors.Should().BeEmpty();
	}

	[Fact]
	public async Task FailedEntry_KeepsItsOwnDayOpenAndClosesTheOthers()
	{
		_failingTickets.Add("A-2");

		var result = await RunAsync(Entry(Monday, "A-1"), Entry(Monday, "A-2", hour: 10), Entry(Tuesday, "B-1"));

		_closedDays.Should().Equal(Tuesday);
		result.Value!.SuccessfulEntries.Should().Be(2);
		result.Value.FailedEntries.Should().Be(1);
	}

	[Fact]
	public async Task FailedEntry_IsReportedAtItsPositionInWhatWasHandedOver()
	{
		// Days run in date order, so Tuesday's entry handed over first is uploaded second
		_failingTickets.Add("B-1");
		_failingTickets.Add("A-2");

		var result = await RunAsync(Entry(Tuesday, "B-1"), Entry(Monday, "A-1"), Entry(Monday, "A-2", hour: 10));

		result.Value!.Errors.Select(e => e.WorklogIndex).Should().BeEquivalentTo([2, 0]);
	}

	[Fact]
	public async Task FailedEntry_NextToOneAlreadyUploaded_KeepsItsOwnPosition()
	{
		_closeFails = true;
		await RunAsync(Entry(Monday, "A-1"));
		_closeFails = false;
		_failingTickets.Add("A-2");

		var result = await RunAsync(Entry(Monday, "A-1"), Entry(Monday, "A-2", hour: 10));

		result.Value!.Errors.Should().ContainSingle().Which.WorklogIndex.Should().Be(1);
	}

	[Fact]
	public async Task FixedEntry_OnRetry_ClosesItsDayAndLeavesTheOtherDaysAlone()
	{
		_failingTickets.Add("A-2");
		await RunAsync(Entry(Monday, "A-1"), Entry(Monday, "A-2", hour: 10), Entry(Tuesday, "B-1"));
		_closedDays.Clear();
		_uploaded.Clear();

		var fixedEntry = Entry(Monday, "A-3", hour: 10);
		var result = await RunAsync(fixedEntry);

		_uploaded.Should().Equal(fixedEntry);
		_closedDays.Should().Equal(Monday);
		result.Value!.FailedEntries.Should().Be(0);
	}

	[Fact]
	public async Task EntryStillFailingOnRetry_KeepsTheDayOpen()
	{
		_failingTickets.Add("A-1");
		await RunAsync(Entry(Monday, "A-1"));

		var result = await RunAsync(Entry(Monday, "A-1"));

		_closedDays.Should().BeEmpty();
		result.Value!.FailedEntries.Should().Be(1);
	}

	[Fact]
	public async Task FailedClose_IsReportedWithoutAFailedEntry()
	{
		_closeFails = true;

		var result = await RunAsync(Entry(Monday, "A-1"));

		result.Value!.FailedEntries.Should().Be(0);
		result.Value.SuccessfulEntries.Should().Be(1);
		result.Value.Errors.Should().ContainSingle()
			.Which.Should().Match<WorklogSubmissionError>(e =>
				e.ErrorMessage.Contains("Timesheet submit failed for 2026-11-02") && e.WorklogIndex == 0);
	}

	[Fact]
	public async Task RetryAfterFailedClose_ClosesTheDayWithoutUploadingTheEntryAgain()
	{
		_closeFails = true;
		await RunAsync(Entry(Monday, "A-1"));
		_closeFails = false;
		_uploaded.Clear();

		var result = await RunAsync(Entry(Monday, "A-1"));

		_uploaded.Should().BeEmpty();
		_closedDays.Should().Equal(Monday);
		result.Value!.SuccessfulEntries.Should().Be(1);
		result.Value.Errors.Should().BeEmpty();
	}

	[Fact]
	public async Task EntryEditedAfterFailedClose_IsUploadedAsNew()
	{
		_closeFails = true;
		await RunAsync(Entry(Monday, "A-1"));
		_closeFails = false;
		_uploaded.Clear();

		var edited = Entry(Monday, "A-1", hour: 11);
		await RunAsync(edited);

		_uploaded.Should().Equal(edited);
	}

	[Fact]
	public async Task EntryOfAClosedDay_SentAgain_IsUploadedAgain()
	{
		await RunAsync(Entry(Monday, "A-1"));
		_uploaded.Clear();

		await RunAsync(Entry(Monday, "A-1"));

		_uploaded.Should().ContainSingle();
	}
}
