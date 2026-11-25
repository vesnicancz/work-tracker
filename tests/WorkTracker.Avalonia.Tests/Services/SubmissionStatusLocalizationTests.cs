using FluentAssertions;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class SubmissionStatusLocalizationTests
{
	private readonly ILocalizationService _localization = CreateLocalization();

	private static ILocalizationService CreateLocalization()
	{
		var localization = new Mock<ILocalizationService>();
		localization
			.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
			.Returns((string key, object[] _) => key);
		return localization.Object;
	}

	[Fact]
	public void FormatSubmissionStatus_AllSuccess_ReturnsSuccessMessage()
	{
		var submission = new SubmissionResult { SuccessfulEntries = 3, FailedEntries = 0 };
		_localization.FormatSubmissionStatus(submission, "Tempo").Should().Contain("SubmissionSuccess");
	}

	[Fact]
	public void FormatSubmissionStatus_AllFailed_ReturnsAllFailedMessage()
	{
		var submission = new SubmissionResult { TotalEntries = 3, SuccessfulEntries = 0, FailedEntries = 3 };
		_localization.FormatSubmissionStatus(submission, "Tempo").Should().Contain("SubmissionAllFailed");
	}

	[Fact]
	public void FormatSubmissionStatus_Partial_ReturnsPartialMessage()
	{
		var submission = new SubmissionResult { SuccessfulEntries = 2, FailedEntries = 1 };
		_localization.FormatSubmissionStatus(submission, "Tempo").Should().Contain("SubmissionPartial");
	}

	[Fact]
	public void FormatSubmissionStatus_ErrorWithoutAFailedEntry_ReturnsPartialMessage()
	{
		var submission = new SubmissionResult
		{
			SuccessfulEntries = 2,
			Errors = [new SubmissionError { ErrorMessage = "Timesheet submit failed" }]
		};
		_localization.FormatSubmissionStatus(submission, "Goran").Should().Contain("SubmissionPartial");
	}
}
