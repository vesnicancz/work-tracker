using FluentAssertions;
using WorkTracker.Domain.Entities;
using WorkTracker.Infrastructure.Repositories;
using WorkTracker.Infrastructure.Tests.Helpers;

namespace WorkTracker.Infrastructure.Tests.Repositories;

/// <summary>
/// Runs against SQLite, so the queries get translated the way they do in the application.
/// </summary>
public sealed class WorkEntryReaderTests : IAsyncDisposable
{
	private static readonly DateTime Day = new(2026, 3, 2);

	private readonly SqliteInMemoryFixture _fixture = new();
	private readonly WorkEntryReader _reader;

	public WorkEntryReaderTests()
	{
		_reader = new WorkEntryReader(_fixture.ContextFactory);
	}

	public ValueTask DisposeAsync() => _fixture.DisposeAsync();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private async Task<WorkEntry> SeedAsync(string ticketId, DateTime start, DateTime? end)
	{
		var entry = WorkEntry.Create(ticketId, start, end, null, start);
		await using var context = _fixture.CreateVerificationContext();
		context.WorkEntries.Add(entry);
		await context.SaveChangesAsync(Ct);
		return entry;
	}

	[Fact]
	public async Task GetByIdAsync_ReturnsTheEntryOrNull()
	{
		var entry = await SeedAsync("A-1", Day.AddHours(9), Day.AddHours(10));

		(await _reader.GetByIdAsync(entry.Id, Ct))!.TicketId.Should().Be("A-1");
		(await _reader.GetByIdAsync(entry.Id + 1, Ct)).Should().BeNull();
	}

	[Fact]
	public async Task GetActiveWorkEntryAsync_ReturnsWhatIsRunning()
	{
		await SeedAsync("DONE-1", Day.AddHours(9), Day.AddHours(10));
		(await _reader.GetActiveWorkEntryAsync(Ct)).Should().BeNull();

		await SeedAsync("RUNNING-1", Day.AddHours(10), null);

		(await _reader.GetActiveWorkEntryAsync(Ct))!.TicketId.Should().Be("RUNNING-1");
	}

	[Fact]
	public async Task GetByDateAsync_ReturnsThatDayInOrder()
	{
		await SeedAsync("LATER", Day.AddHours(11), Day.AddHours(12));
		await SeedAsync("EARLIER", Day.AddHours(9), Day.AddHours(10));
		await SeedAsync("NEXT-DAY", Day.AddDays(1), Day.AddDays(1).AddHours(1));
		await SeedAsync("PREVIOUS-DAY", Day.AddHours(-1), Day);

		var entries = await _reader.GetByDateAsync(Day.AddHours(15), Ct);

		entries.Select(e => e.TicketId).Should().Equal("EARLIER", "LATER");
	}

	[Fact]
	public async Task GetByDateRangeAsync_IncludesTheWholeLastDay()
	{
		await SeedAsync("IN", Day.AddDays(1).AddHours(23), Day.AddDays(1).AddHours(23.5));
		await SeedAsync("OUT", Day.AddDays(2), Day.AddDays(2).AddHours(1));

		var entries = await _reader.GetByDateRangeAsync(Day, Day.AddDays(1), Ct);

		entries.Select(e => e.TicketId).Should().Equal("IN");
	}

	[Fact]
	public async Task GetOverlappingEntriesAsync_FindsOverlapsAndLeavesOutTheExcludedAndAdjacentOnes()
	{
		var overlapping = await SeedAsync("OVERLAPS", Day.AddHours(9), Day.AddHours(11));
		var excluded = await SeedAsync("EXCLUDED", Day.AddHours(11), Day.AddHours(12));
		await SeedAsync("ADJACENT", Day.AddHours(12), Day.AddHours(13));
		await SeedAsync("RUNNING", Day.AddHours(8), null);

		var entries = await _reader.GetOverlappingEntriesAsync(excluded.Id, Day.AddHours(10), Day.AddHours(12), Ct);

		entries.Select(e => e.TicketId).Should().Equal("RUNNING", overlapping.TicketId);
	}

	[Fact]
	public async Task GetOverlappingEntriesAsync_WithoutEnd_ReachesEverythingLater()
	{
		await SeedAsync("LATER", Day.AddDays(30), Day.AddDays(30).AddHours(1));

		var entries = await _reader.GetOverlappingEntriesAsync(null, Day, null, Ct);

		entries.Select(e => e.TicketId).Should().Equal("LATER");
	}
}
