using FluentAssertions;
using WorkTracker.Domain.Services;

namespace WorkTracker.Application.Tests.Models;

public class OverlapResolutionPlanTests
{
	private static OverlapAdjustment CreateAdjustment(
		OverlapAdjustmentKind kind = OverlapAdjustmentKind.TrimEnd,
		DateTime? originalEnd = null) =>
		new(
			WorkEntryId: 1,
			TicketId: "PROJ-1",
			Description: null,
			Kind: kind,
			OriginalStart: new DateTime(2025, 1, 1, 9, 0, 0),
			OriginalEnd: originalEnd,
			NewStart: null,
			NewEnd: new DateTime(2025, 1, 1, 10, 0, 0));

	[Fact]
	public void IsOnlyClosingActiveEntry_SingleTrimEndWithNullOriginalEnd_ReturnsTrue()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [CreateAdjustment(OverlapAdjustmentKind.TrimEnd, originalEnd: null)]
		};

		plan.IsOnlyClosingActiveEntry.Should().BeTrue();
	}

	[Fact]
	public void IsOnlyClosingActiveEntry_SingleTrimEndWithOriginalEnd_ReturnsFalse()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [CreateAdjustment(OverlapAdjustmentKind.TrimEnd, originalEnd: new DateTime(2025, 1, 1, 11, 0, 0))]
		};

		plan.IsOnlyClosingActiveEntry.Should().BeFalse();
	}

	[Fact]
	public void IsOnlyClosingActiveEntry_DeleteWithNullOriginalEnd_ReturnsFalse()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [CreateAdjustment(OverlapAdjustmentKind.Delete, originalEnd: null)]
		};

		plan.IsOnlyClosingActiveEntry.Should().BeFalse();
	}

	[Fact]
	public void IsOnlyClosingActiveEntry_MultipleAdjustments_ReturnsFalse()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments =
			[
				CreateAdjustment(OverlapAdjustmentKind.TrimEnd, originalEnd: null),
				CreateAdjustment(OverlapAdjustmentKind.TrimStart, originalEnd: new DateTime(2025, 1, 1, 12, 0, 0))
			]
		};

		plan.IsOnlyClosingActiveEntry.Should().BeFalse();
	}

	[Fact]
	public void IsOnlyClosingActiveEntry_NoAdjustments_ReturnsFalse()
	{
		var plan = new OverlapResolutionPlan();

		plan.IsOnlyClosingActiveEntry.Should().BeFalse();
	}
}
