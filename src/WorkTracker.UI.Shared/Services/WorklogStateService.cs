using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Domain.Services;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service that manages the current worklog tracking state of the application.
/// Provides a single source of truth for active work tracking.
/// </summary>
public sealed class WorklogStateService : IWorklogStateService, IDisposable
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<WorklogStateService> _logger;
	private readonly SemaphoreSlim _stateLock = new(1, 1);

	private bool _isInitialized;
	private WorkEntry? _activeWork;
	private bool _isTracking;

	public WorklogStateService(
		IServiceScopeFactory scopeFactory,
		ILogger<WorklogStateService> logger)
	{
		_scopeFactory = scopeFactory;
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
			return _isTracking;
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

			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			// Load active work from database
			_activeWork = await workEntryService.GetActiveWorkAsync();
			_isTracking = _activeWork != null;

			_isInitialized = true;

			_logger.LogInformation(
				"WorklogStateService initialized. IsTracking={IsTracking}, ActiveWork={ActiveWorkId}",
				_isTracking,
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
		_logger.LogInformation(
			"Starting work tracking: TicketId={TicketId}, Description={Description}",
			ticketId,
			description);

		return ExecuteLockedAsync<WorkEntry>(
			(svc, ct) => svc.StartWorkAsync(ticketId, null, description, cancellationToken: ct),
			result =>
			{
				UpdateState(result.Value, true);
				_logger.LogInformation("Work tracking started successfully: WorkEntryId={WorkEntryId}", result.Value.Id);
				return Task.CompletedTask;
			},
			"StartTracking",
			cancellationToken);
	}

	public async Task<Result> StopTrackingAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		WorkEntry? oldActiveWork = null;
		var oldIsTracking = false;
		var workEntriesModified = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;
			oldIsTracking = _isTracking;

			if (!_isTracking)
			{
				_logger.LogDebug("No active work to stop");
				return Result.Success();
			}

			_logger.LogInformation("Stopping work tracking: WorkEntryId={WorkEntryId}", _activeWork?.Id);

			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var result = await workEntryService.StopWorkAsync(cancellationToken: cancellationToken);

			if (result.IsSuccess)
			{
				UpdateState(null, false);
				_logger.LogInformation("Work tracking stopped successfully");
				workEntriesModified = true;
			}
			else
			{
				_logger.LogWarning("Failed StopTracking: {Error}", result.Error);
			}

			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error in StopTracking");
			return Result.Failure($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified);
		}
	}

	public async Task RefreshFromDatabaseAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		WorkEntry? oldActiveWork = null;
		var oldIsTracking = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;
			oldIsTracking = _isTracking;

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

		RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified: false);
	}

	public Task<Result<WorkEntry>> CreateWorkEntryAsync(
		string? ticketId,
		DateTime startTime,
		string? description,
		DateTime? endTime,
		CancellationToken cancellationToken)
	{
		_logger.LogInformation(
			"Creating work entry: TicketId={TicketId}, StartTime={StartTime}, EndTime={EndTime}, Description={Description}",
			ticketId,
			startTime,
			endTime,
			description);

		return ExecuteLockedAsync<WorkEntry>(
			(svc, ct) => svc.StartWorkAsync(ticketId, startTime, description, endTime, ct),
			async result =>
			{
				_logger.LogInformation("Work entry created successfully: WorkEntryId={WorkEntryId}", result.Value.Id);
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
		_logger.LogInformation(
			"Updating work entry: Id={Id}, TicketId={TicketId}, StartTime={StartTime}, EndTime={EndTime}",
			id,
			ticketId,
			startTime,
			endTime);

		return ExecuteLockedAsync(
			// UpdateWorkEntryAsync returns Result<WorkEntry>; upcast to Result (value unused)
			async (svc, ct) => (Result)await svc.UpdateWorkEntryAsync(id, ticketId, startTime, endTime, description, ct),
			async _ =>
			{
				_logger.LogInformation("Work entry updated successfully: WorkEntryId={WorkEntryId}", id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"UpdateWorkEntry",
			cancellationToken);
	}

	public Task<Result> DeleteWorkEntryAsync(int id, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Deleting work entry: Id={Id}", id);

		return ExecuteLockedAsync(
			(svc, ct) => svc.DeleteWorkEntryAsync(id, ct),
			async _ =>
			{
				_logger.LogInformation("Work entry deleted successfully: WorkEntryId={WorkEntryId}", id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"DeleteWorkEntry",
			cancellationToken);
	}

	public async Task NotifyWorkEntriesModifiedAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogInformation("Work entries modified notification received (external)");

		WorkEntry? oldActiveWork = null;
		var oldIsTracking = false;
		var refreshed = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;
			oldIsTracking = _isTracking;

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

		RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified: refreshed);
	}

	public async Task<OverlapResolutionPlan> ComputeOverlapResolutionAsync(
		int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		using var scope = _scopeFactory.CreateScope();
		var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

		return await workEntryService.ComputeOverlapResolutionAsync(excludeEntryId, startTime, endTime, cancellationToken);
	}

	public Task<Result<WorkEntry>> CreateWorkEntryWithResolutionAsync(
		string? ticketId, DateTime startTime, string? description, DateTime? endTime,
		OverlapResolutionPlan plan, CancellationToken cancellationToken)
	{
		_logger.LogInformation(
			"Creating work entry with overlap resolution: TicketId={TicketId}, StartTime={StartTime}, EndTime={EndTime}",
			ticketId, startTime, endTime);

		return ExecuteLockedAsync<WorkEntry>(
			(svc, ct) => svc.CreateWithOverlapResolutionAsync(ticketId, startTime, description, endTime, plan, ct),
			async result =>
			{
				_logger.LogInformation("Work entry created with resolution: WorkEntryId={WorkEntryId}", result.Value.Id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"CreateWorkEntryWithResolution",
			cancellationToken);
	}

	public Task<Result> UpdateWorkEntryWithResolutionAsync(
		int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description,
		OverlapResolutionPlan plan, CancellationToken cancellationToken)
	{
		_logger.LogInformation(
			"Updating work entry with overlap resolution: Id={Id}, TicketId={TicketId}",
			id, ticketId);

		return ExecuteLockedAsync(
			async (svc, ct) => (Result)await svc.UpdateWithOverlapResolutionAsync(id, ticketId, startTime, endTime, description, plan, ct),
			async _ =>
			{
				_logger.LogInformation("Work entry updated with resolution: WorkEntryId={WorkEntryId}", id);
				await RefreshFromDatabaseCoreAsync(cancellationToken);
			},
			"UpdateWorkEntryWithResolution",
			cancellationToken);
	}

	#endregion State Operations

	#region Locked Execution Helpers

	/// <summary>
	/// Executes a state-modifying operation under the state lock, with scope creation,
	/// exception handling, and event raising. For operations returning Result{T}.
	/// </summary>
	private async Task<Result<T>> ExecuteLockedAsync<T>(
		Func<IWorkEntryService, CancellationToken, Task<Result<T>>> operation,
		Func<Result<T>, Task> onSuccess,
		string operationName,
		CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		WorkEntry? oldActiveWork = null;
		var oldIsTracking = false;
		var workEntriesModified = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;
			oldIsTracking = _isTracking;

			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var result = await operation(workEntryService, cancellationToken);

			if (result.IsSuccess)
			{
				await onSuccess(result);
				workEntriesModified = true;
			}
			else
			{
				_logger.LogWarning("Failed {Operation}: {Error}", operationName, result.Error);
			}

			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error in {Operation}", operationName);
			return Result.Failure<T>($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified);
		}
	}

	/// <summary>
	/// Executes a state-modifying operation under the state lock, with scope creation,
	/// exception handling, and event raising. For operations returning Result.
	/// </summary>
	private async Task<Result> ExecuteLockedAsync(
		Func<IWorkEntryService, CancellationToken, Task<Result>> operation,
		Func<Result, Task> onSuccess,
		string operationName,
		CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		WorkEntry? oldActiveWork = null;
		var oldIsTracking = false;
		var workEntriesModified = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			oldActiveWork = _activeWork;
			oldIsTracking = _isTracking;

			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var result = await operation(workEntryService, cancellationToken);

			if (result.IsSuccess)
			{
				await onSuccess(result);
				workEntriesModified = true;
			}
			else
			{
				_logger.LogWarning("Failed {Operation}: {Error}", operationName, result.Error);
			}

			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error in {Operation}", operationName);
			return Result.Failure($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified);
		}
	}

	#endregion Locked Execution Helpers

	#region Private Helpers

	private async Task RefreshFromDatabaseCoreAsync(CancellationToken cancellationToken)
	{
		_logger.LogDebug("Refreshing state from database");

		using var scope = _scopeFactory.CreateScope();
		var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

		var activeWork = await workEntryService.GetActiveWorkAsync(cancellationToken);

		UpdateState(activeWork, activeWork != null);

		_logger.LogDebug("State refreshed: IsTracking={IsTracking}", _isTracking);
	}

	/// <summary>
	/// Updates the backing fields. Must be called while holding _stateLock.
	/// Events are NOT raised here — callers must raise them after releasing the lock.
	/// </summary>
	private void UpdateState(WorkEntry? activeWork, bool isTracking)
	{
		_activeWork = activeWork;
		_isTracking = isTracking;
	}

	/// <summary>
	/// Raises events for state changes. Must be called AFTER releasing _stateLock
	/// to prevent deadlocks if subscribers call back into this service.
	/// </summary>
	private void RaiseEvents(WorkEntry? oldActiveWork, bool oldIsTracking, bool workEntriesModified)
	{
		if (_activeWork != oldActiveWork)
		{
			_logger.LogDebug("Raising ActiveWorkChanged event: ActiveWorkId={ActiveWorkId}", _activeWork?.Id);
			ActiveWorkChanged?.Invoke(this, _activeWork);
		}

		if (_isTracking != oldIsTracking)
		{
			_logger.LogDebug("Raising IsTrackingChanged event: IsTracking={IsTracking}", _isTracking);
			IsTrackingChanged?.Invoke(this, _isTracking);
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
