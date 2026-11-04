using Microsoft.Extensions.Logging;
using WorkTracker.Application.Interfaces;

namespace WorkTracker.UI.Shared.Services;

public sealed class ExternalWorkChangeMonitor : IExternalWorkChangeMonitor, IDisposable
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

	private readonly IWorklogStateService _worklogStateService;
	private readonly IWorkDataChangeDetector _changeDetector;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<ExternalWorkChangeMonitor> _logger;
	private readonly CancellationTokenSource _cts = new();
	private readonly Lock _lock = new();

	private ITimer? _timer;
	private DateTime? _knownChange;
	private int _isChecking;

	public ExternalWorkChangeMonitor(
		IWorklogStateService worklogStateService,
		IWorkDataChangeDetector changeDetector,
		TimeProvider timeProvider,
		ILogger<ExternalWorkChangeMonitor> logger)
	{
		_worklogStateService = worklogStateService;
		_changeDetector = changeDetector;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public void Start()
	{
		if (_timer != null)
		{
			return;
		}

		RememberCurrentChange();
		_worklogStateService.WorkEntriesModified += OnWorkEntriesModified;
		_timer = _timeProvider.CreateTimer(OnTimerTick, null, PollInterval, PollInterval);
	}

	/// <summary>
	/// The application's own writes move the file time too. They have already been handled, and
	/// reloading the entries again would only clear the selection in the grid.
	/// </summary>
	private void OnWorkEntriesModified(object? sender, EventArgs e) => RememberCurrentChange();

	private void OnTimerTick(object? state)
	{
		_ = CheckForChangesAsync(_cts.Token)
			.SafeFireAndForgetAsync(ex => _logger.LogWarning(ex, "Checking for external changes to work entries failed"));
	}

	internal async Task CheckForChangesAsync(CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref _isChecking, 1) == 1)
		{
			return;
		}

		try
		{
			var change = _changeDetector.GetLastChangeUtc();

			// Null says nothing about the data, e.g. a removable drive that is not connected right now
			if (change == null)
			{
				return;
			}

			lock (_lock)
			{
				if (change == _knownChange)
				{
					return;
				}

				_knownChange = change;
			}

			_logger.LogInformation("Work entries changed outside of the application, reloading");
			await _worklogStateService.NotifyWorkEntriesModifiedAsync(cancellationToken);
		}
		finally
		{
			Volatile.Write(ref _isChecking, 0);
		}
	}

	private void RememberCurrentChange()
	{
		var change = _changeDetector.GetLastChangeUtc();

		lock (_lock)
		{
			_knownChange = change ?? _knownChange;
		}
	}

	public void Dispose()
	{
		_timer?.Dispose();
		_worklogStateService.WorkEntriesModified -= OnWorkEntriesModified;
		_cts.Cancel();
		_cts.Dispose();
	}
}
