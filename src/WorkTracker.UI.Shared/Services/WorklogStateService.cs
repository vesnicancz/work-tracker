using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
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
			if (_isInitialized) return;

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

	public async Task<Result<WorkEntry>> StartTrackingAsync(
		string? ticketId,
		string? description,
		CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogInformation(
			"Starting work tracking: TicketId={TicketId}, Description={Description}",
			ticketId,
			description);

		var oldActiveWork = _activeWork;
		var oldIsTracking = _isTracking;
		var workEntriesModified = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var result = await workEntryService.StartWorkAsync(ticketId, null, description, cancellationToken: cancellationToken);

			if (result.IsSuccess)
			{
				UpdateState(result.Value, true);
				workEntriesModified = true;

				_logger.LogInformation("Work tracking started successfully: WorkEntryId={WorkEntryId}", result.Value.Id);
			}
			else
			{
				_logger.LogWarning("Failed to start work tracking: {Error}", result.Error);
			}

			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error starting work tracking");
			return Result.Failure<WorkEntry>($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified);
		}
	}

	public async Task<Result> StopTrackingAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		var oldActiveWork = _activeWork;
		var oldIsTracking = _isTracking;
		var workEntriesModified = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
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
				workEntriesModified = true;

				_logger.LogInformation("Work tracking stopped successfully");
			}
			else
			{
				_logger.LogWarning("Failed to stop work tracking: {Error}", result.Error);
			}

			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error stopping work tracking");
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

		var oldActiveWork = _activeWork;
		var oldIsTracking = _isTracking;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
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
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified: false);
		}
	}

	public async Task<Result<WorkEntry>> CreateWorkEntryAsync(
		string? ticketId,
		DateTime startTime,
		string? description,
		DateTime? endTime,
		CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogInformation(
			"Creating work entry: TicketId={TicketId}, StartTime={StartTime}, EndTime={EndTime}, Description={Description}",
			ticketId,
			startTime,
			endTime,
			description);

		var oldActiveWork = _activeWork;
		var oldIsTracking = _isTracking;
		var workEntriesModified = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var result = await workEntryService.StartWorkAsync(ticketId, startTime, description, endTime, cancellationToken);

			if (result.IsSuccess)
			{
				_logger.LogInformation("Work entry created successfully: WorkEntryId={WorkEntryId}", result.Value.Id);

				await RefreshFromDatabaseCoreAsync(cancellationToken);
				workEntriesModified = true;
			}
			else
			{
				_logger.LogWarning("Failed to create work entry: {Error}", result.Error);
			}

			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error creating work entry");
			return Result.Failure<WorkEntry>($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified);
		}
	}

	public async Task<Result> UpdateWorkEntryAsync(
		int id,
		string? ticketId,
		DateTime startTime,
		DateTime? endTime,
		string? description,
		CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogInformation(
			"Updating work entry: Id={Id}, TicketId={TicketId}, StartTime={StartTime}, EndTime={EndTime}",
			id,
			ticketId,
			startTime,
			endTime);

		var oldActiveWork = _activeWork;
		var oldIsTracking = _isTracking;
		var workEntriesModified = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var result = await workEntryService.UpdateWorkEntryAsync(id, ticketId, startTime, endTime, description, cancellationToken);

			if (result.IsSuccess)
			{
				_logger.LogInformation("Work entry updated successfully: WorkEntryId={WorkEntryId}", id);

				await RefreshFromDatabaseCoreAsync(cancellationToken);
				workEntriesModified = true;
			}
			else
			{
				_logger.LogWarning("Failed to update work entry: {Error}", result.Error);
			}

			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error updating work entry");
			return Result.Failure($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified);
		}
	}

	public async Task<Result> DeleteWorkEntryAsync(int id, CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogInformation("Deleting work entry: Id={Id}", id);

		var oldActiveWork = _activeWork;
		var oldIsTracking = _isTracking;
		var workEntriesModified = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var result = await workEntryService.DeleteWorkEntryAsync(id, cancellationToken);

			if (result.IsSuccess)
			{
				_logger.LogInformation("Work entry deleted successfully: WorkEntryId={WorkEntryId}", id);

				await RefreshFromDatabaseCoreAsync(cancellationToken);
				workEntriesModified = true;
			}
			else
			{
				_logger.LogWarning("Failed to delete work entry: {Error}", result.Error);
			}

			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error deleting work entry");
			return Result.Failure($"Unexpected error: {ex.Message}");
		}
		finally
		{
			_stateLock.Release();
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified);
		}
	}

	public async Task NotifyWorkEntriesModifiedAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogInformation("Work entries modified notification received (external)");

		var oldActiveWork = _activeWork;
		var oldIsTracking = _isTracking;
		var refreshed = false;

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
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
			RaiseEvents(oldActiveWork, oldIsTracking, workEntriesModified: refreshed);
		}
	}

	#endregion State Operations

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
