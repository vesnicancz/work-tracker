namespace WorkTracker.Application.Models;

public sealed class OverlapResolutionPlan
{
	public IReadOnlyList<OverlapAdjustment> Adjustments { get; init; } = [];

	public bool HasAdjustments => Adjustments.Count > 0;

	public bool IsOnlyClosingActiveEntry =>
		Adjustments is [{ Kind: OverlapAdjustmentKind.TrimEnd, OriginalEnd: null }];
}
