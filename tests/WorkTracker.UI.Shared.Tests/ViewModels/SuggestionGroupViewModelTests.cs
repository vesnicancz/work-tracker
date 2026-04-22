using FluentAssertions;
using Moq;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.ViewModels;

public class SuggestionGroupViewModelTests
{
	private readonly Mock<IWorkSuggestionOrchestrator> _orchestrator = new();

	[Fact]
	public void Ctor_ViewingToday_MarksFinishedEventsAsPast()
	{
		var now = new DateTime(2026, 4, 22, 14, 30, 0);
		var timeProvider = new FakeTimeProvider(now);
		var group = new SuggestionGroup
		{
			PluginId = "cal",
			PluginName = "Calendar",
			Items =
			[
				CreateEvent(new DateTime(2026, 4, 22, 9, 0, 0), new DateTime(2026, 4, 22, 10, 0, 0)),
				CreateEvent(new DateTime(2026, 4, 22, 14, 0, 0), new DateTime(2026, 4, 22, 15, 0, 0)),
				CreateEvent(new DateTime(2026, 4, 22, 16, 0, 0), new DateTime(2026, 4, 22, 17, 0, 0)),
			],
		};

		var vm = new SuggestionGroupViewModel(_orchestrator.Object, group, now.Date, timeProvider);

		vm.Items[0].IsPast.Should().BeTrue("event that already ended");
		vm.Items[1].IsPast.Should().BeFalse("event still in progress");
		vm.Items[2].IsPast.Should().BeFalse("event that hasn't started yet");
	}

	[Fact]
	public void Ctor_ViewingOtherDay_DoesNotMarkItemsAsPast()
	{
		var now = new DateTime(2026, 4, 22, 14, 30, 0);
		var timeProvider = new FakeTimeProvider(now);
		var yesterday = new DateTime(2026, 4, 21);
		var group = new SuggestionGroup
		{
			PluginId = "cal",
			PluginName = "Calendar",
			Items =
			[
				CreateEvent(yesterday.AddHours(9), yesterday.AddHours(10)),
				CreateEvent(yesterday.AddHours(14), yesterday.AddHours(15)),
			],
		};

		var vm = new SuggestionGroupViewModel(_orchestrator.Object, group, yesterday, timeProvider);

		vm.Items.Should().OnlyContain(i => !i.IsPast);
	}

	[Fact]
	public void Ctor_EventWithOnlyStartTime_UsesStartTimeAsCutoff()
	{
		var now = new DateTime(2026, 4, 22, 14, 30, 0);
		var timeProvider = new FakeTimeProvider(now);
		var group = new SuggestionGroup
		{
			PluginId = "cal",
			PluginName = "Calendar",
			Items =
			[
				CreateEvent(new DateTime(2026, 4, 22, 10, 0, 0), endTime: null),
				CreateEvent(new DateTime(2026, 4, 22, 18, 0, 0), endTime: null),
			],
		};

		var vm = new SuggestionGroupViewModel(_orchestrator.Object, group, now.Date, timeProvider);

		vm.Items[0].IsPast.Should().BeTrue();
		vm.Items[1].IsPast.Should().BeFalse();
	}

	[Fact]
	public void Ctor_ItemWithoutTimeSlot_IsNotMarkedAsPast()
	{
		var now = new DateTime(2026, 4, 22, 14, 30, 0);
		var timeProvider = new FakeTimeProvider(now);
		var group = new SuggestionGroup
		{
			PluginId = "jira",
			PluginName = "Jira",
			Items = [CreateTicket("PROJ-1")],
		};

		var vm = new SuggestionGroupViewModel(_orchestrator.Object, group, now.Date, timeProvider);

		vm.Items[0].IsPast.Should().BeFalse();
	}

	private static WorkSuggestionViewModel CreateEvent(DateTime startTime, DateTime? endTime) => new()
	{
		Title = "Meeting",
		StartTime = startTime,
		EndTime = endTime,
		Source = "calendar",
		SourceId = startTime.ToString("O"),
	};

	private static WorkSuggestionViewModel CreateTicket(string ticketId) => new()
	{
		Title = ticketId,
		TicketId = ticketId,
		Source = "jira",
		SourceId = ticketId,
	};

	private sealed class FakeTimeProvider : TimeProvider
	{
		private readonly DateTime _localNow;

		public FakeTimeProvider(DateTime localNow)
		{
			_localNow = localNow;
		}

		public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

		public override DateTimeOffset GetUtcNow() => new(_localNow, TimeSpan.Zero);
	}
}
