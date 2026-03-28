using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Services;

public class WorklogStateServiceTests : IDisposable
{
	private readonly Mock<IWorkEntryService> _mockWorkEntryService = new();
	private readonly Mock<IServiceScopeFactory> _mockScopeFactory = new();
	private readonly WorklogStateService _sut;

	public WorklogStateServiceTests()
	{
		var mockScope = new Mock<IServiceScope>();
		var mockProvider = new Mock<IServiceProvider>();

		_mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
		mockScope.Setup(s => s.ServiceProvider).Returns(mockProvider.Object);
		mockProvider.Setup(p => p.GetService(typeof(IWorkEntryService))).Returns(_mockWorkEntryService.Object);

		_sut = new WorklogStateService(
			_mockScopeFactory.Object,
			new Mock<ILogger<WorklogStateService>>().Object);
	}

	public void Dispose()
	{
		_sut.Dispose();
	}

	private static WorkEntry CreateActiveEntry(int id = 1, string ticketId = "PROJ-1") =>
		WorkEntry.Reconstitute(id, ticketId, DateTime.Now.AddHours(-1), null, "desc", true, DateTime.Now.AddHours(-1));

	private static WorkEntry CreateCompletedEntry(int id = 1, string ticketId = "PROJ-1") =>
		WorkEntry.Reconstitute(id, ticketId, DateTime.Now.AddHours(-2), DateTime.Now.AddHours(-1), "desc", false, DateTime.Now.AddHours(-2));

	private async Task InitializeSut(WorkEntry? activeWork = null)
	{
		_mockWorkEntryService
			.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(activeWork);
		await _sut.InitializeAsync();
	}

	#region Before initialization

	[Fact]
	public void ActiveWork_BeforeInit_ThrowsInvalidOperationException()
	{
		var act = () => _sut.ActiveWork;
		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void IsTracking_BeforeInit_ThrowsInvalidOperationException()
	{
		var act = () => _sut.IsTracking;
		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void IsInitialized_BeforeInit_ReturnsFalse()
	{
		_sut.IsInitialized.Should().BeFalse();
	}

	#endregion

	#region InitializeAsync

	[Fact]
	public async Task InitializeAsync_WithActiveWork_SetsState()
	{
		var activeEntry = CreateActiveEntry();
		await InitializeSut(activeEntry);

		_sut.IsInitialized.Should().BeTrue();
		_sut.IsTracking.Should().BeTrue();
		_sut.ActiveWork.Should().NotBeNull();
		_sut.ActiveWork!.Id.Should().Be(activeEntry.Id);
	}

	[Fact]
	public async Task InitializeAsync_WithNoActiveWork_SetsNotTracking()
	{
		await InitializeSut(null);

		_sut.IsInitialized.Should().BeTrue();
		_sut.IsTracking.Should().BeFalse();
		_sut.ActiveWork.Should().BeNull();
	}

	[Fact]
	public async Task InitializeAsync_CalledTwice_IsIdempotent()
	{
		var activeEntry = CreateActiveEntry();
		_mockWorkEntryService
			.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(activeEntry);

		await _sut.InitializeAsync();
		await _sut.InitializeAsync();

		_mockWorkEntryService.Verify(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()), Times.Once);
	}

	#endregion

	#region StartTrackingAsync

	[Fact]
	public async Task StartTrackingAsync_Success_UpdatesStateAndFiresEvents()
	{
		await InitializeSut(null);

		var newEntry = CreateActiveEntry();
		_mockWorkEntryService
			.Setup(s => s.StartWorkAsync("PROJ-1", null, "desc", null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(newEntry));

		WorkEntry? receivedActiveWork = null;
		bool? receivedIsTracking = null;
		var workEntriesModifiedFired = false;

		_sut.ActiveWorkChanged += (_, work) => receivedActiveWork = work;
		_sut.IsTrackingChanged += (_, tracking) => receivedIsTracking = tracking;
		_sut.WorkEntriesModified += (_, _) => workEntriesModifiedFired = true;

		var result = await _sut.StartTrackingAsync("PROJ-1", "desc", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		_sut.IsTracking.Should().BeTrue();
		_sut.ActiveWork.Should().NotBeNull();

		receivedActiveWork.Should().NotBeNull();
		receivedIsTracking.Should().BeTrue();
		workEntriesModifiedFired.Should().BeTrue();
	}

	[Fact]
	public async Task StartTrackingAsync_Failure_NoStateChange()
	{
		await InitializeSut(null);

		_mockWorkEntryService
			.Setup(s => s.StartWorkAsync(It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("Overlap detected"));

		var isTrackingChanged = false;
		_sut.IsTrackingChanged += (_, _) => isTrackingChanged = true;

		var result = await _sut.StartTrackingAsync("PROJ-1", "desc", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		_sut.IsTracking.Should().BeFalse();
		isTrackingChanged.Should().BeFalse();
	}

	[Fact]
	public async Task StartTrackingAsync_BeforeInit_ThrowsInvalidOperationException()
	{
		var act = () => _sut.StartTrackingAsync("PROJ-1", "desc", TestContext.Current.CancellationToken);
		await act.Should().ThrowAsync<InvalidOperationException>();
	}

	#endregion

	#region StopTrackingAsync

	[Fact]
	public async Task StopTrackingAsync_WhenTracking_StopsAndFiresEvents()
	{
		var activeEntry = CreateActiveEntry();
		await InitializeSut(activeEntry);

		var stoppedEntry = CreateCompletedEntry();
		_mockWorkEntryService
			.Setup(s => s.StopWorkAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(stoppedEntry));

		bool? receivedIsTracking = null;
		var workEntriesModifiedFired = false;
		_sut.IsTrackingChanged += (_, tracking) => receivedIsTracking = tracking;
		_sut.WorkEntriesModified += (_, _) => workEntriesModifiedFired = true;

		var result = await _sut.StopTrackingAsync(TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		_sut.IsTracking.Should().BeFalse();
		_sut.ActiveWork.Should().BeNull();
		receivedIsTracking.Should().BeFalse();
		workEntriesModifiedFired.Should().BeTrue();
	}

	[Fact]
	public async Task StopTrackingAsync_WhenNotTracking_ReturnsSuccessNoEvents()
	{
		await InitializeSut(null);

		var isTrackingChanged = false;
		_sut.IsTrackingChanged += (_, _) => isTrackingChanged = true;

		var result = await _sut.StopTrackingAsync(TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		isTrackingChanged.Should().BeFalse();
	}

	[Fact]
	public async Task StopTrackingAsync_Failure_NoStateChange()
	{
		var activeEntry = CreateActiveEntry();
		await InitializeSut(activeEntry);

		_mockWorkEntryService
			.Setup(s => s.StopWorkAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("DB error"));

		var result = await _sut.StopTrackingAsync(TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		_sut.IsTracking.Should().BeTrue();
	}

	#endregion

	#region RefreshFromDatabaseAsync

	[Fact]
	public async Task RefreshFromDatabaseAsync_UpdatesFromDatabase()
	{
		await InitializeSut(null);

		var newActiveEntry = CreateActiveEntry(2, "PROJ-NEW");
		_mockWorkEntryService
			.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(newActiveEntry);

		WorkEntry? receivedActiveWork = null;
		_sut.ActiveWorkChanged += (_, work) => receivedActiveWork = work;

		await _sut.RefreshFromDatabaseAsync(TestContext.Current.CancellationToken);

		_sut.IsTracking.Should().BeTrue();
		_sut.ActiveWork!.Id.Should().Be(2);
		receivedActiveWork.Should().NotBeNull();
	}

	[Fact]
	public async Task RefreshFromDatabaseAsync_NoChange_NoEvents()
	{
		await InitializeSut(null);

		_mockWorkEntryService
			.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		var activeWorkChanged = false;
		_sut.ActiveWorkChanged += (_, _) => activeWorkChanged = true;

		await _sut.RefreshFromDatabaseAsync(TestContext.Current.CancellationToken);

		activeWorkChanged.Should().BeFalse();
	}

	#endregion

	#region CreateWorkEntryAsync

	[Fact]
	public async Task CreateWorkEntryAsync_Success_RefreshesAndFiresEvents()
	{
		await InitializeSut(null);

		var now = DateTime.Now;
		var newEntry = CreateCompletedEntry(5, "PROJ-5");
		_mockWorkEntryService
			.Setup(s => s.StartWorkAsync("PROJ-5", now, "work", now.AddHours(1), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(newEntry));
		_mockWorkEntryService
			.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		var workEntriesModifiedFired = false;
		_sut.WorkEntriesModified += (_, _) => workEntriesModifiedFired = true;

		var result = await _sut.CreateWorkEntryAsync("PROJ-5", now, "work", now.AddHours(1), TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		workEntriesModifiedFired.Should().BeTrue();
	}

	#endregion

	#region UpdateWorkEntryAsync

	[Fact]
	public async Task UpdateWorkEntryAsync_Success_RefreshesAndFiresEvents()
	{
		await InitializeSut(null);

		var now = DateTime.Now;
		var updatedEntry = CreateCompletedEntry(1, "PROJ-UPD");
		_mockWorkEntryService
			.Setup(s => s.UpdateWorkEntryAsync(1, "PROJ-UPD", now, now.AddHours(1), "updated", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(updatedEntry));
		_mockWorkEntryService
			.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		var workEntriesModifiedFired = false;
		_sut.WorkEntriesModified += (_, _) => workEntriesModifiedFired = true;

		var result = await _sut.UpdateWorkEntryAsync(1, "PROJ-UPD", now, now.AddHours(1), "updated", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		workEntriesModifiedFired.Should().BeTrue();
	}

	#endregion

	#region DeleteWorkEntryAsync

	[Fact]
	public async Task DeleteWorkEntryAsync_Success_RefreshesAndFiresEvents()
	{
		await InitializeSut(null);

		_mockWorkEntryService
			.Setup(s => s.DeleteWorkEntryAsync(1, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());
		_mockWorkEntryService
			.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		var workEntriesModifiedFired = false;
		_sut.WorkEntriesModified += (_, _) => workEntriesModifiedFired = true;

		var result = await _sut.DeleteWorkEntryAsync(1, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		workEntriesModifiedFired.Should().BeTrue();
	}

	#endregion

	#region NotifyWorkEntriesModifiedAsync

	[Fact]
	public async Task NotifyWorkEntriesModifiedAsync_RefreshesAndFiresEvent()
	{
		await InitializeSut(null);

		_mockWorkEntryService
			.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		var workEntriesModifiedFired = false;
		_sut.WorkEntriesModified += (_, _) => workEntriesModifiedFired = true;

		await _sut.NotifyWorkEntriesModifiedAsync(TestContext.Current.CancellationToken);

		workEntriesModifiedFired.Should().BeTrue();
	}

	#endregion

	#region Dispose

	[Fact]
	public void Dispose_DoesNotThrow()
	{
		var act = () => _sut.Dispose();
		act.Should().NotThrow();
	}

	#endregion
}
