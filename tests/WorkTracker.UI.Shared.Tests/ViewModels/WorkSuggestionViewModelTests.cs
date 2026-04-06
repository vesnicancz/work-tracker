using FluentAssertions;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.ViewModels;

public class WorkSuggestionViewModelTests
{
	private static WorkSuggestionViewModel Create(
		string? ticketId = null,
		DateTime? startTime = null,
		DateTime? endTime = null) => new()
	{
		Title = "Test Task",
		TicketId = ticketId,
		StartTime = startTime,
		EndTime = endTime,
		Source = "test",
		SourceId = "test-1"
	};

	// --- HasTimeSlot ---

	[Fact]
	public void HasTimeSlot_WithStartTime_ReturnsTrue()
	{
		var vm = Create(startTime: new DateTime(2026, 4, 6, 9, 0, 0));

		vm.HasTimeSlot.Should().BeTrue();
	}

	[Fact]
	public void HasTimeSlot_WithoutStartTime_ReturnsFalse()
	{
		var vm = Create();

		vm.HasTimeSlot.Should().BeFalse();
	}

	// --- TimeDisplay ---

	[Fact]
	public void TimeDisplay_WithStartAndEnd_ReturnsRange()
	{
		var vm = Create(
			startTime: new DateTime(2026, 4, 6, 9, 0, 0),
			endTime: new DateTime(2026, 4, 6, 10, 30, 0));

		vm.TimeDisplay.Should().Be("09:00 – 10:30");
	}

	[Fact]
	public void TimeDisplay_WithStartOnly_ReturnsSingleTime()
	{
		var vm = Create(startTime: new DateTime(2026, 4, 6, 14, 15, 0));

		vm.TimeDisplay.Should().Be("14:15");
	}

	[Fact]
	public void TimeDisplay_WithoutStartTime_ReturnsEmpty()
	{
		var vm = Create();

		vm.TimeDisplay.Should().BeEmpty();
	}

	// --- BadgeText ---

	[Fact]
	public void BadgeText_WithTimeSlot_ReturnsTimeDisplay()
	{
		var vm = Create(
			ticketId: "PROJ-123",
			startTime: new DateTime(2026, 4, 6, 9, 0, 0),
			endTime: new DateTime(2026, 4, 6, 10, 0, 0));

		vm.BadgeText.Should().Be("09:00 – 10:00");
	}

	[Fact]
	public void BadgeText_WithoutTimeSlot_ReturnsTicketId()
	{
		var vm = Create(ticketId: "PROJ-456");

		vm.BadgeText.Should().Be("PROJ-456");
	}

	[Fact]
	public void BadgeText_WithoutTimeSlotOrTicket_ReturnsNull()
	{
		var vm = Create();

		vm.BadgeText.Should().BeNull();
	}

	// --- IsBadgeAccent ---

	[Fact]
	public void IsBadgeAccent_TicketWithoutTimeSlot_ReturnsTrue()
	{
		var vm = Create(ticketId: "PROJ-789");

		vm.IsBadgeAccent.Should().BeTrue();
	}

	[Fact]
	public void IsBadgeAccent_TicketWithTimeSlot_ReturnsFalse()
	{
		var vm = Create(
			ticketId: "PROJ-789",
			startTime: new DateTime(2026, 4, 6, 9, 0, 0));

		vm.IsBadgeAccent.Should().BeFalse();
	}

	[Fact]
	public void IsBadgeAccent_NoTicket_ReturnsFalse()
	{
		var vm = Create();

		vm.IsBadgeAccent.Should().BeFalse();
	}
}
