using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.Orchestrators;

public class WorklogSubmissionOrchestratorTests
{
	private readonly Mock<IWorklogSubmissionService> _mockSubmissionService;
	private readonly Mock<ILocalizationService> _mockLocalization;
	private readonly WorklogSubmissionOrchestrator _orchestrator;

	public WorklogSubmissionOrchestratorTests()
	{
		_mockSubmissionService = new Mock<IWorklogSubmissionService>();
		_mockLocalization = new Mock<ILocalizationService>();
		_mockLocalization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		_mockLocalization.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
			.Returns((string key, object[] args) => $"{key}:{string.Join(",", args)}");

		_orchestrator = new WorklogSubmissionOrchestrator(
			_mockSubmissionService.Object,
			_mockLocalization.Object,
			new Mock<ILogger<WorklogSubmissionOrchestrator>>().Object);
	}

	#region LoadAvailableProviders

	[Fact]
	public void LoadAvailableProviders_ReturnsProviders()
	{
		var providers = new List<ProviderInfo>
		{
			new() { Id = "tempo", Name = "Tempo" },
			new() { Id = "jira", Name = "Jira" }
		};
		_mockSubmissionService.Setup(s => s.GetAvailableProviders()).Returns(providers);

		var result = _orchestrator.LoadAvailableProviders();

		result.Should().HaveCount(2);
	}

	[Fact]
	public void LoadAvailableProviders_OnException_ReturnsEmptyList()
	{
		_mockSubmissionService.Setup(s => s.GetAvailableProviders()).Throws<InvalidOperationException>();

		var result = _orchestrator.LoadAvailableProviders();

		result.Should().BeEmpty();
	}

	#endregion LoadAvailableProviders

	#region LoadPreviewAsync

	[Fact]
	public async Task LoadPreviewAsync_Daily_ReturnsItems()
	{
		var date = new DateTime(2025, 1, 15);
		var dto = new WorklogSubmissionDto
		{
			Worklogs = new List<WorklogDto>
			{
				new() { TicketId = "PROJ-1", Description = "Work", DurationMinutes = 60, StartTime = date.AddHours(9), EndTime = date.AddHours(10) },
				new() { TicketId = "PROJ-2", Description = "More", DurationMinutes = 30, StartTime = date.AddHours(10), EndTime = date.AddHours(10).AddMinutes(30) }
			}
		};
		_mockSubmissionService.Setup(s => s.PreviewDailyWorklogAsync(date, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

		var result = await _orchestrator.LoadPreviewAsync(date, false, "No ticket", TestContext.Current.CancellationToken);

		result.Items.Should().HaveCount(2);
		result.DataItemCount.Should().Be(2);
		result.TotalSeconds.Should().Be(90 * 60);
	}

	[Fact]
	public async Task LoadPreviewAsync_Daily_EmptyWorklogs_ReturnsEmpty()
	{
		var dto = new WorklogSubmissionDto { Worklogs = new List<WorklogDto>() };
		_mockSubmissionService.Setup(s => s.PreviewDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);

		var result = await _orchestrator.LoadPreviewAsync(FixedDate, false, "No ticket", TestContext.Current.CancellationToken);

		result.Items.Should().BeEmpty();
		result.DataItemCount.Should().Be(0);
		result.TotalSeconds.Should().Be(0);
	}

	[Fact]
	public async Task LoadPreviewAsync_Daily_NullTicket_UsesNoTicketLabel()
	{
		var date = new DateTime(2025, 1, 15);
		var dto = new WorklogSubmissionDto
		{
			Worklogs = new List<WorklogDto>
			{
				new() { TicketId = null, Description = "Work", DurationMinutes = 60, StartTime = date.AddHours(9), EndTime = date.AddHours(10) }
			}
		};
		_mockSubmissionService.Setup(s => s.PreviewDailyWorklogAsync(date, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

		var result = await _orchestrator.LoadPreviewAsync(date, false, "No ticket", TestContext.Current.CancellationToken);

		result.Items.Should().ContainSingle();
		result.Items[0].TicketId.Should().BeNull();
		result.Items[0].NoTicketLabel.Should().Be("No ticket");
		result.Items[0].TicketIdDisplay.Should().Be("No ticket");
	}

	[Fact]
	public async Task LoadPreviewAsync_Weekly_AddsDateHeaders()
	{
		var monday = new DateTime(2025, 1, 13);
		var tuesday = new DateTime(2025, 1, 14);
		var weeklyPreview = new Dictionary<DateTime, WorklogSubmissionDto>
		{
			[monday] = new() { Worklogs = new List<WorklogDto> { new() { TicketId = "A-1", DurationMinutes = 60, StartTime = monday.AddHours(9), EndTime = monday.AddHours(10) } } },
			[tuesday] = new() { Worklogs = new List<WorklogDto> { new() { TicketId = "A-2", DurationMinutes = 30, StartTime = tuesday.AddHours(9), EndTime = tuesday.AddHours(9).AddMinutes(30) } } }
		};
		_mockSubmissionService.Setup(s => s.PreviewWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(weeklyPreview);

		var result = await _orchestrator.LoadPreviewAsync(monday, true, "No ticket", TestContext.Current.CancellationToken);

		result.Items.Should().HaveCount(4); // 2 headers + 2 data items
		result.DataItemCount.Should().Be(2);
		result.Items[0].IsDateHeader.Should().BeTrue();
		result.Items[1].IsDateHeader.Should().BeFalse();
	}

	#endregion LoadPreviewAsync

	#region SubmitAsync

	[Fact]
	public async Task SubmitAsync_AllSuccess_ReturnsAllSucceeded()
	{
		var items = CreatePreviewItems("PROJ-1");
		var submission = new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1, FailedEntries = 0 };
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), "tempo", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(submission));

		var outcome = await _orchestrator.SubmitAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		outcome.AllSucceeded.Should().BeTrue();
		outcome.HasFailedItems.Should().BeFalse();
	}

	[Fact]
	public async Task SubmitAsync_WithFailedEntries_MarksFailedItems()
	{
		var items = CreatePreviewItems("PROJ-1");
		var submission = new SubmissionResult
		{
			TotalEntries = 1,
			SuccessfulEntries = 0,
			FailedEntries = 1,
			Errors = new List<SubmissionError>
			{
				new() { TicketId = "PROJ-1", Date = FixedDate, ErrorMessage = "Conflict", Details = $"{items[0].StartTime:HH:mm}-{items[0].EndTime:HH:mm}" }
			}
		};
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), "tempo", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(submission));

		var outcome = await _orchestrator.SubmitAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		outcome.AllSucceeded.Should().BeFalse();
		outcome.HasFailedItems.Should().BeTrue();
		items[0].HasError.Should().BeTrue();
	}

	[Fact]
	public async Task SubmitAsync_ClearsPreviousErrorsOnSuccess()
	{
		var items = CreatePreviewItems("PROJ-1");
		items[0].HasError = true;
		items[0].ErrorMessage = "old";

		var submission = new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1, FailedEntries = 0 };
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), "tempo", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(submission));

		await _orchestrator.SubmitAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		items[0].HasError.Should().BeFalse();
		items[0].ErrorMessage.Should().BeNull();
	}

	[Fact]
	public async Task SubmitAsync_AllFailed_StatusContainsAllFailed()
	{
		var items = CreatePreviewItems("PROJ-1");
		var submission = new SubmissionResult
		{
			TotalEntries = 1,
			SuccessfulEntries = 0,
			FailedEntries = 1,
			Errors = new List<SubmissionError> { new() { TicketId = "PROJ-1", Date = FixedDate, ErrorMessage = "err", Details = $"{items[0].StartTime:HH:mm}-{items[0].EndTime:HH:mm}" } }
		};
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), "tempo", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(submission));

		var outcome = await _orchestrator.SubmitAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		outcome.StatusMessage.Should().Contain("SubmissionAllFailed");
	}

	[Fact]
	public async Task SubmitAsync_ServiceFailure_ReturnsFailure()
	{
		var items = CreatePreviewItems("PROJ-1");
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), "tempo", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<SubmissionResult>("Network error"));

		var outcome = await _orchestrator.SubmitAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		outcome.AllSucceeded.Should().BeFalse();
		outcome.StatusMessage.Should().Contain("Network error");
	}

	#endregion SubmitAsync

	#region RetryFailedAsync

	[Fact]
	public async Task RetryFailedAsync_NoFailedItems_ReturnsSuccess()
	{
		var items = CreatePreviewItems("PROJ-1");

		var outcome = await _orchestrator.RetryFailedAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		outcome.AllSucceeded.Should().BeTrue();
		outcome.HasFailedItems.Should().BeFalse();
	}

	[Fact]
	public async Task RetryFailedAsync_RetriesOnlyFailed()
	{
		var items = CreatePreviewItems("PROJ-1");
		items[0].HasError = true;

		var submission = new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1, FailedEntries = 0 };
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), "tempo", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(submission));

		var outcome = await _orchestrator.RetryFailedAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		outcome.AllSucceeded.Should().BeTrue();
		_mockSubmissionService.Verify(s => s.SubmitCustomWorklogsAsync(
			It.Is<IEnumerable<WorklogDto>>(w => w.Count() == 1), "tempo", It.IsAny<CancellationToken>()), Times.Once);
	}

	#endregion RetryFailedAsync

	#region MarkFailedItems

	[Fact]
	public void MarkFailedItems_NoErrors_ReturnsFalse()
	{
		var items = CreatePreviewItems("PROJ-1");
		var submission = new SubmissionResult { Errors = new List<SubmissionError>() };

		_orchestrator.MarkFailedItems(items, submission).Should().BeFalse();
		items[0].HasError.Should().BeFalse();
	}

	[Fact]
	public void MarkFailedItems_WithMatchingError_MarksItem()
	{
		var items = CreatePreviewItems("PROJ-1");
		var submission = new SubmissionResult
		{
			Errors = new List<SubmissionError>
			{
				new() { TicketId = "PROJ-1", Date = FixedDate, ErrorMessage = "Conflict", Details = $"{items[0].StartTime:HH:mm}-{items[0].EndTime:HH:mm}" }
			}
		};

		_orchestrator.MarkFailedItems(items, submission).Should().BeTrue();
		items[0].HasError.Should().BeTrue();
		items[0].ErrorMessage.Should().Be("Conflict");
	}

	[Fact]
	public void MarkFailedItems_ClearsPreviousErrors()
	{
		var items = CreatePreviewItems("PROJ-1");
		items[0].HasError = true;
		items[0].ErrorMessage = "Old error";

		var submission = new SubmissionResult { Errors = new List<SubmissionError>() };

		_orchestrator.MarkFailedItems(items, submission);
		items[0].HasError.Should().BeFalse();
		items[0].ErrorMessage.Should().BeNull();
	}

	#endregion MarkFailedItems

	#region FormatSubmissionStatus

	[Fact]
	public void FormatSubmissionStatus_AllSuccess_ReturnsSuccessMessage()
	{
		var submission = new SubmissionResult { SuccessfulEntries = 3, FailedEntries = 0 };
		_orchestrator.FormatSubmissionStatus(submission, "Tempo").Should().Contain("SubmissionSuccess");
	}

	[Fact]
	public void FormatSubmissionStatus_AllFailed_ReturnsAllFailedMessage()
	{
		var submission = new SubmissionResult { TotalEntries = 3, SuccessfulEntries = 0, FailedEntries = 3 };
		_orchestrator.FormatSubmissionStatus(submission, "Tempo").Should().Contain("SubmissionAllFailed");
	}

	[Fact]
	public void FormatSubmissionStatus_Partial_ReturnsPartialMessage()
	{
		var submission = new SubmissionResult { SuccessfulEntries = 2, FailedEntries = 1 };
		_orchestrator.FormatSubmissionStatus(submission, "Tempo").Should().Contain("SubmissionPartial");
	}

	#endregion FormatSubmissionStatus

	#region ResetItems

	[Fact]
	public void ResetItems_RestoresOriginalValues()
	{
		var items = CreatePreviewItems("PROJ-1");
		items[0].SaveOriginalValues();
		items[0].TicketId = "CHANGED";
		items[0].HasError = true;
		items[0].ErrorMessage = "error";

		_orchestrator.ResetItems(items);

		items[0].TicketId.Should().Be("PROJ-1");
		items[0].HasError.Should().BeFalse();
		items[0].ErrorMessage.Should().BeNull();
	}

	[Fact]
	public void ResetItems_SkipsDateHeaders()
	{
		var items = new List<WorklogPreviewItem>
		{
			new() { IsDateHeader = true, DateDisplay = "Monday" },
			new() { TicketId = "PROJ-1", Date = FixedDate, StartTime = FixedDate.AddHours(9), EndTime = FixedDate.AddHours(10), Duration = 3600 }
		};
		items[1].SaveOriginalValues();
		items[1].TicketId = "CHANGED";

		_orchestrator.ResetItems(items);

		items[0].IsDateHeader.Should().BeTrue();
		items[1].TicketId.Should().Be("PROJ-1");
	}

	[Fact]
	public void ResetItems_RestoresIsSelectedToTrue()
	{
		var items = CreatePreviewItems("PROJ-1");
		items[0].IsSelected = false;

		_orchestrator.ResetItems(items);

		items[0].IsSelected.Should().BeTrue();
	}

	#endregion ResetItems

	#region InvertSelection

	[Fact]
	public void InvertSelection_TogglesAllDataItems()
	{
		var items = new List<WorklogPreviewItem>
		{
			new() { IsDateHeader = true, DateDisplay = "Monday" },
			new() { TicketId = "A-1", Date = FixedDate, StartTime = FixedDate.AddHours(9), EndTime = FixedDate.AddHours(10), Duration = 3600 },
			new() { TicketId = "A-2", Date = FixedDate, StartTime = FixedDate.AddHours(10), EndTime = FixedDate.AddHours(11), Duration = 3600, IsSelected = false }
		};

		_orchestrator.InvertSelection(items);

		items[0].IsDateHeader.Should().BeTrue();
		items[1].IsSelected.Should().BeFalse();
		items[2].IsSelected.Should().BeTrue();
	}

	[Fact]
	public void InvertSelection_SkipsDateHeaders()
	{
		var header = new WorklogPreviewItem { IsDateHeader = true, DateDisplay = "Monday" };
		var items = new List<WorklogPreviewItem> { header };

		_orchestrator.InvertSelection(items);

		header.IsDateHeader.Should().BeTrue();
	}

	#endregion InvertSelection

	#region SelectAll

	[Fact]
	public void SelectAll_SetsAllDataItemsToSelected()
	{
		var items = new List<WorklogPreviewItem>
		{
			new() { IsDateHeader = true, DateDisplay = "Monday" },
			new() { TicketId = "A-1", Date = FixedDate, StartTime = FixedDate.AddHours(9), EndTime = FixedDate.AddHours(10), Duration = 3600, IsSelected = false },
			new() { TicketId = "A-2", Date = FixedDate, StartTime = FixedDate.AddHours(10), EndTime = FixedDate.AddHours(11), Duration = 3600, IsSelected = false }
		};

		_orchestrator.SelectAll(items);

		items[1].IsSelected.Should().BeTrue();
		items[2].IsSelected.Should().BeTrue();
	}

	[Fact]
	public void SelectAll_SkipsDateHeaders()
	{
		var header = new WorklogPreviewItem { IsDateHeader = true, DateDisplay = "Monday" };
		var items = new List<WorklogPreviewItem> { header };

		_orchestrator.SelectAll(items);

		header.IsDateHeader.Should().BeTrue();
	}

	#endregion SelectAll

	#region IsSelected

	[Fact]
	public void WorklogPreviewItem_IsSelected_DefaultsToTrue()
	{
		var item = new WorklogPreviewItem();

		item.IsSelected.Should().BeTrue();
	}

	[Fact]
	public async Task SubmitAsync_OnlySubmitsSelectedItems()
	{
		var item1 = new WorklogPreviewItem
		{
			TicketId = "PROJ-1", Description = "Work 1", Date = FixedDate, Duration = 3600,
			StartTime = FixedDate.AddHours(9), EndTime = FixedDate.AddHours(10)
		};
		item1.SaveOriginalValues();

		var item2 = new WorklogPreviewItem
		{
			TicketId = "PROJ-2", Description = "Work 2", Date = FixedDate, Duration = 1800,
			StartTime = FixedDate.AddHours(10), EndTime = FixedDate.AddHours(10).AddMinutes(30),
			IsSelected = false
		};
		item2.SaveOriginalValues();

		var items = new List<WorklogPreviewItem> { item1, item2 };

		var submission = new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1, FailedEntries = 0 };
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), "tempo", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(submission));

		await _orchestrator.SubmitAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		_mockSubmissionService.Verify(s => s.SubmitCustomWorklogsAsync(
			It.Is<IEnumerable<WorklogDto>>(w => w.Count() == 1 && w.First().TicketId == "PROJ-1"),
			"tempo", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task RetryFailedAsync_OnlyRetriesSelectedFailedItems()
	{
		var item1 = new WorklogPreviewItem
		{
			TicketId = "PROJ-1", Description = "Work 1", Date = FixedDate, Duration = 3600,
			StartTime = FixedDate.AddHours(9), EndTime = FixedDate.AddHours(10),
			HasError = true
		};
		item1.SaveOriginalValues();

		var item2 = new WorklogPreviewItem
		{
			TicketId = "PROJ-2", Description = "Work 2", Date = FixedDate, Duration = 1800,
			StartTime = FixedDate.AddHours(10), EndTime = FixedDate.AddHours(10).AddMinutes(30),
			HasError = true, IsSelected = false
		};
		item2.SaveOriginalValues();

		var items = new List<WorklogPreviewItem> { item1, item2 };

		var submission = new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1, FailedEntries = 0 };
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), "tempo", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(submission));

		await _orchestrator.RetryFailedAsync(items, "tempo", "Tempo", TestContext.Current.CancellationToken);

		_mockSubmissionService.Verify(s => s.SubmitCustomWorklogsAsync(
			It.Is<IEnumerable<WorklogDto>>(w => w.Count() == 1 && w.First().TicketId == "PROJ-1"),
			"tempo", It.IsAny<CancellationToken>()), Times.Once);
	}

	#endregion IsSelected

	private static readonly DateTime FixedDate = new(2025, 6, 15);

	private static List<WorklogPreviewItem> CreatePreviewItems(string ticketId)
	{
		var item = new WorklogPreviewItem
		{
			TicketId = ticketId,
			Description = "Test work",
			Date = FixedDate,
			Duration = 3600,
			StartTime = FixedDate.AddHours(9),
			EndTime = FixedDate.AddHours(10)
		};
		item.SaveOriginalValues();
		return [item];
	}
}