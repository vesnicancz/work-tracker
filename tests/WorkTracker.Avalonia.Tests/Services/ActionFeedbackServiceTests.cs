using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class ActionFeedbackServiceTests
{
	private readonly Mock<INotificationService> _toasts = new();
	private readonly Mock<ISystemNotificationService> _systemNotifications = new();
	private readonly Mock<IUiDispatcher> _uiDispatcher = new();
	private readonly ActionFeedbackService _service;

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	public ActionFeedbackServiceTests()
	{
		_uiDispatcher
			.Setup(d => d.InvokeAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
			.Returns((Func<Task<bool>> action, CancellationToken _) => action());

		_service = new ActionFeedbackService(
			_toasts.Object,
			_systemNotifications.Object,
			_uiDispatcher.Object,
			NullLogger<ActionFeedbackService>.Instance);
	}

	[Fact]
	public async Task ReportSuccess_WhileUserSeesTheWindow_ShowsOnlyAToast()
	{
		_toasts.Setup(t => t.IsVisibleToUser).Returns(true);

		await _service.ReportSuccessAsync("Started", TestToken);

		_toasts.Verify(t => t.ShowSuccess("Started"), Times.Once);
		_systemNotifications.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task ReportFailure_WhileUserSeesTheWindow_ShowsOnlyAnErrorToast()
	{
		_toasts.Setup(t => t.IsVisibleToUser).Returns(true);

		await _service.ReportFailureAsync("Nope", TestToken);

		_toasts.Verify(t => t.ShowError("Nope"), Times.Once);
		_systemNotifications.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task Report_WhileWindowIsOutOfSight_ReplacesThePreviousSystemNotification()
	{
		await _service.ReportSuccessAsync("Started", TestToken);
		await _service.ReportFailureAsync("Nope", TestToken);

		_systemNotifications.Verify(n => n.ShowReplacingNotificationAsync("WorkTracker", "Started"), Times.Once);
		_systemNotifications.Verify(n => n.ShowReplacingNotificationAsync("WorkTracker", "Nope"), Times.Once);
		_toasts.Verify(t => t.ShowSuccess(It.IsAny<string>()), Times.Never);
		_toasts.Verify(t => t.ShowError(It.IsAny<string>()), Times.Never);
	}

	[Fact]
	public async Task Report_DecidesOnTheUiThread()
	{
		_uiDispatcher
			.Setup(d => d.InvokeAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		await _service.ReportSuccessAsync("Started", TestToken);

		_toasts.Verify(t => t.IsVisibleToUser, Times.Never);
		_uiDispatcher.Verify(d => d.InvokeAsync(It.IsAny<Func<Task<bool>>>(), TestToken), Times.Once);
	}

	[Theory]
	[InlineData(typeof(OperationCanceledException))]
	[InlineData(typeof(InvalidOperationException))]
	public async Task Report_WhenItCannotBeShown_DoesNotThrow(Type exceptionType)
	{
		_uiDispatcher
			.Setup(d => d.InvokeAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync((Exception)Activator.CreateInstance(exceptionType)!);

		var report = () => _service.ReportFailureAsync("Nope", TestToken);

		await report.Should().NotThrowAsync();
	}
}
