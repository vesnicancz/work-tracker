using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Orchestrators;

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
	public async Task LoadPreviewAsync_Daily_AsksForTheDayAndPassesItsWorklogsOn()
	{
		var date = new DateTime(2025, 1, 15);
		IReadOnlyList<WorklogSubmissionDto> days = [new WorklogSubmissionDto { SubmissionDate = date }];
		_mockSubmissionService
			.Setup(s => s.PreviewAsync(SubmissionPeriod.Day(date), WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()))
			.ReturnsAsync(days);

		var result = await _orchestrator.LoadPreviewAsync(date, false, WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		result.Should().BeSameAs(days);
	}

	[Fact]
	public async Task LoadPreviewAsync_Weekly_AsksForTheWeekInTheGivenMode()
	{
		var monday = new DateTime(2025, 1, 13);
		_mockSubmissionService
			.Setup(s => s.PreviewAsync(It.IsAny<SubmissionPeriod>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);

		await _orchestrator.LoadPreviewAsync(monday, true, WorklogSubmissionMode.Aggregated, TestContext.Current.CancellationToken);

		_mockSubmissionService.Verify(s => s.PreviewAsync(SubmissionPeriod.WeekOf(monday), WorklogSubmissionMode.Aggregated, It.IsAny<CancellationToken>()), Times.Once);
	}

	#endregion LoadPreviewAsync

	#region SubmitAsync

	private static readonly IReadOnlyList<WorklogDto> Worklogs = [new WorklogDto { TicketId = "PROJ-1", DurationMinutes = 60 }];

	private void SetupSubmission(Result<SubmissionResult> result)
	{
		_mockSubmissionService
			.Setup(s => s.SubmitCustomWorklogsAsync(Worklogs, "tempo", WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()))
			.ReturnsAsync(result);
	}

	private Task<SubmissionOutcome> SubmitAsync() =>
		_orchestrator.SubmitAsync(Worklogs, "tempo", "Tempo", WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

	[Fact]
	public async Task SubmitAsync_AllSuccess_IsDeliveredWithoutErrors()
	{
		SetupSubmission(Result.Success(new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var outcome = await SubmitAsync();

		outcome.Delivered.Should().BeTrue();
		outcome.Errors.Should().BeEmpty();
		outcome.StatusMessage.Should().Contain("SubmissionSuccess");
	}

	[Fact]
	public async Task SubmitAsync_WithFailedEntries_PassesTheErrorsOn()
	{
		var error = new SubmissionError { ErrorMessage = "Conflict", WorklogIndex = 0 };
		SetupSubmission(Result.Success(new SubmissionResult { TotalEntries = 1, FailedEntries = 1, Errors = [error] }));

		var outcome = await SubmitAsync();

		outcome.Delivered.Should().BeTrue();
		outcome.Errors.Should().Equal(error);
		outcome.StatusMessage.Should().Contain("SubmissionAllFailed");
	}

	[Fact]
	public async Task SubmitAsync_ServiceFailure_IsNotDelivered()
	{
		SetupSubmission(Result.Failure<SubmissionResult>("Network error"));

		var outcome = await SubmitAsync();

		outcome.Delivered.Should().BeFalse();
		outcome.Errors.Should().BeEmpty();
		outcome.StatusMessage.Should().Contain("Network error");
	}

	#endregion SubmitAsync

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

	[Fact]
	public void FormatSubmissionStatus_ErrorWithoutAFailedEntry_ReturnsPartialMessage()
	{
		var submission = new SubmissionResult
		{
			SuccessfulEntries = 2,
			Errors = [new SubmissionError { ErrorMessage = "Timesheet submit failed" }]
		};
		_orchestrator.FormatSubmissionStatus(submission, "Goran").Should().Contain("SubmissionPartial");
	}

	#endregion FormatSubmissionStatus
}
