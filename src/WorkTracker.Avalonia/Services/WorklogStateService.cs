using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Domain.Services;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Service that manages the current worklog tracking state of the application.
/// Provides a single source of truth for active work tracking.
/// </summary>
public sealed class WorklogStateService : IWorklogStateService, IDisposable
{
	private readonly IWorkEntryService _workEntryService;
	private readonly IUiDispatcher _uiDispatcher;
	private readonly ILogger<WorklogStateService> _logger;
	private readonly SemaphoreSlim _stateLock = new(1, 1);

	private bool _isInitialized;
	private WorkEntry? _activeWork;

	public WorklogStateService(
		IWorkEntryService workEntryService,
		IUiDispatcher uiDispatcher,
		ILogger<WorklogStateService> logger)
	{
		_workEntryService = workEntryService;
		_uiDispatcher = uiDispatcher;
		_logger = logger;
	}

	#region Properties

	public bool IsInitialized => _isInitialized;

	public WorkEntry? ActiveWork
	{
		get
		{
			ThrowIfNotInitialized();
			return _activeWork;
		}
	}

	public bool IsTracking
	{
		get
		{
			ThrowIfNotInitialized();
			return _activeWork != null;
		}
	}

	#endregion Properties

	#region Events

	public event EventHandler<WorkEntry?>? ActiveWorkChanged;

	public event EventHandler<bool>? IsTrackingChanged;

	public event EventHandler? WorkEntriesModified;

	#endregion Events

	#region Initialization

	public async Task InitializeAsync()
	{
		if (_isInitialized)
		{
			_logger.LogDebug("WorklogStateService already initialized, skipping");
			return;
		}

		_logger.LogInformation("Initializing WorklogStateService");

		await _stateLock.WaitAsync();
		try
		{
			if (_isInitialized)
			{
				return;
			}

			// Load active work from database
			_activeWork = await _workEntryService.GetActiveWorkAsync();

			_isInitialized = true;

			_logger.LogInformation(
				"WorklogStateService initialized. IsTracking={IsTracking}, ActiveWork={ActiveWorkId}",
				_activeWork != null,
				_activeWork?.Id);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to initialize WorklogStateService");
			throw;
		}
		finally
		{
			_stateLock.Release();
		}
	}

	#endregion Initialization

	#region State Operations

	public Task<Result<WorkEntry>> StartTrackingAsync(
		string? ticketId,
		string? description,
		CancellationToken cancellationToken)
	{
		_logger.LogDebug(
			"Starting work tracking for ticket {TicketId}",
			ticketId);

		return ExecuteLockedAsync<WorkEntry>(
			(svc, ct) => svc.StartWorkAsync(ticketId, null, description, cancellationToken: ct),
			result =>
			{
				_activeWork = result.Value;
				_logger.LogDebug("Work tracking started successfully with ID {Id}", result.Value.Id);
				return Task.CompletedTask;
			},
			"StartTracking",
			cancellationToken);
	}

	public async Task<Result> StopTrackingAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		WorkEntry? oldActiveWork = null;
		var workEntriesModified = false;
		Result result;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;

			if (_activeWork == null)
			{
				_logger.LogDebug("No active work to stop");
				return Result.Success();
			}

			_logger.LogDebug("Stopping work tracking for entry {Id}", _activeWork?.Id);

			result = await _workEntryService.StopWorkAsync(cancellationToken: cancellationToken);

			if (result.IsSuccess)
			{
				_activeWork = null;
				_logger.LogDebug("Work tracking stopped successfully");
				workEntriesModified = true;
			}
			else
			{
				_logger.LogWarning("Failed StopTracking: {Error}", result.Error);
			}
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error in StopTracking");
			result = Result.Failure($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
		}

		await RaiseEventsAsync(oldActiveWork, workEntriesModified);
		return result;
	}

	public async Task RefreshFromDatabaseAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		WorkEntry? oldActiveWork = null;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;

			await RefreshFromDatabaseCoreAsync(cancellationToken);
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to refresh state from database");
			throw;
		}
		finally
		{
			_stateLock.Release();
		}

		await RaiseEventsAsync(oldActiveWork, workEntriesModified: false);
	}

	public Task<Result<WorkEntry>> CreateWorkEntryAsync(
		string? ticketId,
		DateTime startTime,
		string? description,
		DateTime? endTime,
		CancellationToken cancellationToken)
	{
		_logger.LogDebug(
			"Creating work entry for ticket {TicketId} from {StartTime} to {EndTime}",
			ticketId,
			startTime,
			endTime);

		return ExecuteLockedAsync<WorkEntry>(
			(svc, ct) => svc.StartWorkAsync(ticketId, startTime, description, endTime, ct),
			async result =>
			{
				_logger.LogDebug("Work entry created with ID {Id}", result.Value.Id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"CreateWorkEntry",
			cancellationToken);
	}

	public Task<Result> UpdateWorkEntryAsync(
		int id,
		string? ticketId,
		DateTime startTime,
		DateTime? endTime,
		string? description,
		CancellationToken cancellationToken)
	{
		_logger.LogDebug(
			"Updating work entry {Id} for ticket {TicketId}",
			id,
			ticketId);

		return ExecuteLockedAsync(
			// UpdateWorkEntryAsync returns Result<WorkEntry>; upcast to Result (value unused)
			async (svc, ct) => (Result)await svc.UpdateWorkEntryAsync(id, ticketId, startTime, endTime, description, ct),
			async _ =>
			{
				_logger.LogDebug("Work entry {Id} updated successfully", id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"UpdateWorkEntry",
			cancellationToken);
	}

	public Task<Result> ShiftActiveWorkStartAsync(TimeSpan delta, CancellationToken cancellationToken)
	{
		_logger.LogDebug("Shifting the start of active work by {Delta}", delta);

		return ExecuteLockedAsync(
			async (svc, ct) => (Result)await svc.ShiftActiveWorkStartAsync(delta, ct),
			async _ => await RefreshFromDatabaseCoreAsync(cancellationToken),
			"ShiftActiveWorkStart",
			cancellationToken);
	}

	public Task<Result> DeleteWorkEntryAsync(int id, CancellationToken cancellationToken)
	{
		_logger.LogDebug("Deleting work entry {Id}", id);

		return ExecuteLockedAsync(
			(svc, ct) => svc.DeleteWorkEntryAsync(id, ct),
			async _ =>
			{
				_logger.LogDebug("Work entry {Id} deleted successfully", id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"DeleteWorkEntry",
			cancellationToken);
	}

	public async Task NotifyWorkEntriesModifiedAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogDebug("Work entries modified notification received (external)");

		WorkEntry? oldActiveWork = null;
		var refreshed = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;

			await RefreshFromDatabaseCoreAsync(cancellationToken);
			refreshed = true;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to refresh state after work entries modification");
		}
		finally
		{
			_stateLock.Release();
		}

		await RaiseEventsAsync(oldActiveWork, workEntriesModified: refreshed);
	}

	public async Task<OverlapResolutionPlan> ComputeOverlapResolutionAsync(
		int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		return await _workEntryService.ComputeOverlapResolutionAsync(excludeEntryId, startTime, endTime, cancellationToken);
	}

	public Task<Result<WorkEntry>> CreateWorkEntryWithResolutionAsync(
		string? ticketId, DateTime startTime, string? description, DateTime? endTime,
		OverlapResolutionPlan plan, CancellationToken cancellationToken)
	{
		_logger.LogDebug(
			"Creating work entry with overlap resolution for ticket {TicketId}",
			ticketId);

		return ExecuteLockedAsync<WorkEntry>(
			(svc, ct) => svc.CreateWithOverlapResolutionAsync(ticketId, startTime, description, endTime, plan, ct),
			async result =>
			{
				_logger.LogDebug("Work entry created with resolution, ID {Id}", result.Value.Id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"CreateWorkEntryWithResolution",
			cancellationToken);
	}

	public Task<Result> UpdateWorkEntryWithResolutionAsync(
		int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description,
		OverlapResolutionPlan plan, CancellationToken cancellationToken)
	{
		_logger.LogDebug(
			"Updating work entry {Id} with overlap resolution",
			id);

		return ExecuteLockedAsync(
			async (svc, ct) => (Result)await svc.UpdateWithOverlapResolutionAsync(id, ticketId, startTime, endTime, description, plan, ct),
			async _ =>
			{
				_logger.LogDebug("Work entry {Id} updated with resolution", id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"UpdateWorkEntryWithResolution",
			cancellationToken);
	}

	#endregion State Operations

	#region Locked Execution Helpers

	private Task<Result<T>> ExecuteLockedAsync<T>(
		Func<IWorkEntryService, CancellationToken, Task<Result<T>>> operation,
		Func<Result<T>, Task> onSuccess,
		string operationName,
		CancellationToken cancellationToken)
	{
		return ExecuteLockedCoreAsync(operation, onSuccess, Result.Failure<T>, operationName, cancellationToken);
	}

	private Task<Result> ExecuteLockedAsync(
		Func<IWorkEntryService, CancellationToken, Task<Result>> operation,
		Func<Result, Task> onSuccess,
		string operationName,
		CancellationToken cancellationToken)
	{
		return ExecuteLockedCoreAsync(operation, onSuccess, Result.Failure, operationName, cancellationToken);
	}

	/// <summary>
	/// Executes a state-modifying operation under the state lock, with exception handling and
	/// event raising.
	/// </summary>
	private async Task<TResult> ExecuteLockedCoreAsync<TResult>(
		Func<IWorkEntryService, CancellationToken, Task<TResult>> operation,
		Func<TResult, Task> onSuccess,
		Func<string, TResult> toFailure,
		string operationName,
		CancellationToken cancellationToken)
		where TResult : Result
	{
		ThrowIfNotInitialized();

		WorkEntry? oldActiveWork = null;
		var workEntriesModified = false;
		TResult result;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;

			result = await operation(_workEntryService, cancellationToken);

			if (result.IsSuccess)
			{
				await onSuccess(result);
				workEntriesModified = true;
			}
			else
			{
				_logger.LogWarning("Failed {Operation}: {Error}", operationName, result.Error);
			}
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error in {Operation}", operationName);
			result = toFailure($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
		}

		await RaiseEventsAsync(oldActiveWork, workEntriesModified);
		return result;
	}

	#endregion Locked Execution Helpers

	#region Private Helpers

	private async Task RefreshFromDatabaseCoreAsync(CancellationToken cancellationToken)
	{
		_logger.LogDebug("Refreshing state from database");

		var activeWork = await _workEntryService.GetActiveWorkAsync(cancellationToken);

		_activeWork = activeWork;

		_logger.LogDebug("State refreshed: IsTracking={IsTracking}", _activeWork != null);
	}

	/// <summary>
	/// Raises events for state changes on the UI thread, because subscribers touch UI objects and
	/// not every caller starts there: the Pomodoro timer calls in from the thread pool.
	/// Must be called AFTER releasing _stateLock to prevent deadlocks if subscribers call back into
	/// this service. The change is already saved by now, so a failing subscriber is only logged and
	/// does not turn the operation into a failure.
	/// </summary>
	private async Task RaiseEventsAsync(WorkEntry? oldActiveWork, bool workEntriesModified)
	{
		if (_activeWork == oldActiveWork && !workEntriesModified)
		{
			return;
		}

		try
		{
			await _uiDispatcher.InvokeAsync(() =>
			{
				RaiseEvents(oldActiveWork, workEntriesModified);
				return Task.FromResult(true);
			}, CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "A subscriber failed while handling a worklog state change");
		}
	}

	private void RaiseEvents(WorkEntry? oldActiveWork, bool workEntriesModified)
	{
		if (_activeWork != oldActiveWork)
		{
			_logger.LogDebug("Raising ActiveWorkChanged event: ActiveWorkId={ActiveWorkId}", _activeWork?.Id);
			ActiveWorkChanged?.Invoke(this, _activeWork);
		}

		var isTracking = _activeWork != null;
		if (isTracking != (oldActiveWork != null))
		{
			_logger.LogDebug("Raising IsTrackingChanged event: IsTracking={IsTracking}", isTracking);
			IsTrackingChanged?.Invoke(this, isTracking);
		}

		if (workEntriesModified)
		{
			_logger.LogDebug("Raising WorkEntriesModified event");
			WorkEntriesModified?.Invoke(this, EventArgs.Empty);
		}
	}

	#endregion Private Helpers

	#region Validation

	private void ThrowIfNotInitialized()
	{
		if (!_isInitialized)
		{
			throw new InvalidOperationException(
				"WorklogStateService must be initialized before use. " +
				"Call InitializeAsync() first during application startup.");
		}
	}

	#endregion Validation

	public void Dispose()
	{
		_stateLock.Dispose();
	}
}
