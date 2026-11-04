using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Interfaces;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Services;

public class ExternalWorkChangeMonitorTests : IDisposable
{
	private static readonly DateTime FirstChange = new(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime SecondChange = FirstChange.AddMinutes(1);

	private readonly Mock<IWorklogStateService> _worklogStateService = new();
	private readonly Mock<IWorkDataChangeDetector> _changeDetector = new();
	private readonly ExternalWorkChangeMonitor _sut;

	public ExternalWorkChangeMonitorTests()
	{
		_changeDetector.Setup(d => d.GetLastChangeUtc()).Returns(FirstChange);
		_sut = new ExternalWorkChangeMonitor(
			_worklogStateService.Object,
			_changeDetector.Object,
			TimeProvider.System,
			new Mock<ILogger<ExternalWorkChangeMonitor>>().Object);
		_sut.Start();
	}

	public void Dispose()
	{
		_sut.Dispose();
	}

	private void VerifyReloads(Times times) =>
		_worklogStateService.Verify(s => s.NotifyWorkEntriesModifiedAsync(It.IsAny<CancellationToken>()), times);

	[Fact]
	public async Task Check_WhenNothingChanged_DoesNotReload()
	{
		await _sut.CheckForChangesAsync(CancellationToken.None);

		VerifyReloads(Times.Never());
	}

	[Fact]
	public async Task Check_AfterAnExternalChange_ReloadsOnce()
	{
		_changeDetector.Setup(d => d.GetLastChangeUtc()).Returns(SecondChange);

		await _sut.CheckForChangesAsync(CancellationToken.None);
		await _sut.CheckForChangesAsync(CancellationToken.None);

		VerifyReloads(Times.Once());
	}

	[Fact]
	public async Task Check_AfterTheApplicationsOwnChange_DoesNotReload()
	{
		_changeDetector.Setup(d => d.GetLastChangeUtc()).Returns(SecondChange);
		_worklogStateService.Raise(s => s.WorkEntriesModified += null, EventArgs.Empty);

		await _sut.CheckForChangesAsync(CancellationToken.None);

		VerifyReloads(Times.Never());
	}

	[Fact]
	public async Task Check_WhenTheDatabaseIsOutOfReach_DoesNotReloadUntilItIsBackUnchanged()
	{
		_changeDetector.Setup(d => d.GetLastChangeUtc()).Returns((DateTime?)null);
		await _sut.CheckForChangesAsync(CancellationToken.None);

		_changeDetector.Setup(d => d.GetLastChangeUtc()).Returns(FirstChange);
		await _sut.CheckForChangesAsync(CancellationToken.None);

		VerifyReloads(Times.Never());
	}

	[Fact]
	public async Task Check_WhileAReloadIsRunning_DoesNotStartAnother()
	{
		var reload = new TaskCompletionSource();
		_worklogStateService
			.Setup(s => s.NotifyWorkEntriesModifiedAsync(It.IsAny<CancellationToken>()))
			.Returns(reload.Task);
		_changeDetector.Setup(d => d.GetLastChangeUtc()).Returns(SecondChange);

		var running = _sut.CheckForChangesAsync(CancellationToken.None);
		_changeDetector.Setup(d => d.GetLastChangeUtc()).Returns(SecondChange.AddMinutes(1));
		await _sut.CheckForChangesAsync(CancellationToken.None);
		reload.SetResult();
		await running;

		VerifyReloads(Times.Once());
	}
}
