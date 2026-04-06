using FluentAssertions;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Services;
using WorkTracker.Tests.Common.Builders;

namespace WorkTracker.Domain.Tests.Services;

public class OverlapResolverTests
{
	private static readonly DateTime BaseDate = new(2026, 1, 15);

	private static WorkEntry CreateEntry(int id, int startHour, int endHour) =>
		new WorkEntryBuilder()
			.WithId(id)
			.WithTicketId($"PROJ-{id}")
			.WithTimes(At(startHour), At(endHour))
			.Build();

	private static WorkEntry CreateActiveEntry(int id, int startHour) =>
		new WorkEntryBuilder()
			.WithId(id)
			.WithTicketId($"PROJ-{id}")
			.WithStartTime(At(startHour))
			.Active()
			.Build();

	private static DateTime At(int hour, int minute = 0) => BaseDate.AddHours(hour).AddMinutes(minute);

	[Fact]
	public void Resolve_NoOverlaps_ReturnsEmpty()
	{
		var result = OverlapResolver.Resolve([], At(10), At(11));

		result.Should().BeEmpty();
	}

	[Fact]
	public void DetermineAdjustment_CompleteCover_ReturnsDelete()
	{
		var existing = CreateEntry(1, 10, 11);

		var result = OverlapResolver.DetermineAdjustment(existing, At(11), At(9), At(12));

		result.Kind.Should().Be(OverlapAdjustmentKind.Delete);
		result.WorkEntryId.Should().Be(1);
	}

	[Fact]
	public void DetermineAdjustment_HeadOverlap_ReturnsTrimEnd()
	{
		var existing = CreateEntry(1, 9, 11);

		var result = OverlapResolver.DetermineAdjustment(existing, At(11), At(10), At(12));

		result.Kind.Should().Be(OverlapAdjustmentKind.TrimEnd);
		result.NewEnd.Should().Be(At(10));
	}

	[Fact]
	public void DetermineAdjustment_TailOverlap_ReturnsTrimStart()
	{
		var existing = CreateEntry(1, 10, 12);

		var result = OverlapResolver.DetermineAdjustment(existing, At(12), At(9), At(11));

		result.Kind.Should().Be(OverlapAdjustmentKind.TrimStart);
		result.NewStart.Should().Be(At(11));
	}

	[Fact]
	public void DetermineAdjustment_CandidateInsideExisting_ReturnsSplit()
	{
		var existing = CreateEntry(1, 9, 15);

		var result = OverlapResolver.DetermineAdjustment(existing, At(15), At(11), At(13));

		result.Kind.Should().Be(OverlapAdjustmentKind.Split);
		result.NewEnd.Should().Be(At(11));
		result.NewStart.Should().Be(At(13));
	}

	[Fact]
	public void DetermineAdjustment_ActiveEntry_NeverSplit_ReturnsTrimEnd()
	{
		var existing = CreateActiveEntry(1, 9);

		var result = OverlapResolver.DetermineAdjustment(existing, DateTime.MaxValue, At(10), At(12));

		result.Kind.Should().Be(OverlapAdjustmentKind.TrimEnd);
		result.NewEnd.Should().Be(At(10));
	}

	[Fact]
	public void DetermineAdjustment_HeadOverlap_RemainingTooShort_PromotesToDelete()
	{
		// Existing: 10:00 - 10:00:30, candidate starts at 10:00 → remaining 30s < 1 min
		var existingEnd = At(10, 0).AddSeconds(30);
		var existing = new WorkEntryBuilder()
			.WithId(1).WithTicketId("PROJ-1")
			.WithTimes(At(10), existingEnd)
			.Build();

		var result = OverlapResolver.DetermineAdjustment(existing, existingEnd, At(10), At(12));

		result.Kind.Should().Be(OverlapAdjustmentKind.Delete);
	}

	[Fact]
	public void DetermineAdjustment_TailOverlap_RemainingTooShort_PromotesToDelete()
	{
		// Existing: 10:00 - 10:30, candidate ends at 10:29:30 → remaining 30s < 1 min
		var existingEnd = At(10, 30);
		var existing = new WorkEntryBuilder()
			.WithId(1).WithTicketId("PROJ-1")
			.WithTimes(At(10), existingEnd)
			.Build();

		var result = OverlapResolver.DetermineAdjustment(existing, existingEnd, At(9), At(10, 30).AddSeconds(-30));

		result.Kind.Should().Be(OverlapAdjustmentKind.Delete);
	}

	[Fact]
	public void DetermineAdjustment_Split_FirstHalfTooShort_ReturnsTrimStart()
	{
		var existing = CreateEntry(1, 10, 14);

		// Candidate 10:00:30 - 12:00 → first half = 30s < 1 min
		var result = OverlapResolver.DetermineAdjustment(existing, At(14), At(10, 0).AddSeconds(30), At(12));

		result.Kind.Should().Be(OverlapAdjustmentKind.TrimStart);
		result.NewStart.Should().Be(At(12));
	}

	[Fact]
	public void DetermineAdjustment_Split_SecondHalfTooShort_ReturnsTrimEnd()
	{
		var existing = CreateEntry(1, 10, 14);

		// Candidate 12:00 - 13:59:30 → second half = 30s < 1 min
		var result = OverlapResolver.DetermineAdjustment(existing, At(14), At(12), At(13, 59).AddSeconds(30));

		result.Kind.Should().Be(OverlapAdjustmentKind.TrimEnd);
		result.NewEnd.Should().Be(At(12));
	}

	[Fact]
	public void DetermineAdjustment_Split_BothHalvesTooShort_ReturnsDelete()
	{
		// Existing: 10:00 - 10:01, candidate 10:00:20 - 10:00:40 → both halves < 1 min
		var existingEnd = At(10, 1);
		var existing = new WorkEntryBuilder()
			.WithId(1).WithTicketId("PROJ-1")
			.WithTimes(At(10), existingEnd)
			.Build();

		var result = OverlapResolver.DetermineAdjustment(existing, existingEnd, At(10).AddSeconds(20), At(10).AddSeconds(40));

		result.Kind.Should().Be(OverlapAdjustmentKind.Delete);
	}

	[Fact]
	public void Resolve_MultipleOverlaps_ReturnsAdjustmentPerEntry()
	{
		var entries = new List<WorkEntry>
		{
			CreateEntry(1, 9, 11),
			CreateEntry(2, 11, 13)
		};

		var result = OverlapResolver.Resolve(entries, At(10), At(12));

		result.Should().HaveCount(2);
		result[0].WorkEntryId.Should().Be(1);
		result[1].WorkEntryId.Should().Be(2);
	}

	[Fact]
	public void DetermineAdjustment_PreservesOriginalTimes()
	{
		var existing = CreateEntry(1, 9, 12);

		var result = OverlapResolver.DetermineAdjustment(existing, At(12), At(10), At(13));

		result.OriginalStart.Should().Be(At(9));
		result.OriginalEnd.Should().Be(At(12));
	}

	[Fact]
	public void DetermineAdjustment_PreservesTicketAndDescription()
	{
		var existing = new WorkEntryBuilder()
			.WithId(5)
			.WithTicketId("TASK-42")
			.WithTimes(9, 12)
			.WithDescription("Some work")
			.Build();

		var result = OverlapResolver.DetermineAdjustment(existing, At(12), At(10), At(13));

		result.TicketId.Should().Be("TASK-42");
		result.Description.Should().Be("Some work");
	}
}
