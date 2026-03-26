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
public sealed class WorklogStateService : IWorklogStateService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<WorklogStateService> _logger;

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
		private set
		{
			if (_activeWork != value)
			{
				_activeWork = value;
				OnActiveWorkChanged(value);
			}
		}
	}

	public bool IsTracking
	{
		get
		{
			ThrowIfNotInitialized();
			return _isTracking;
		}
		private set
		{
			if (_isTracking != value)
			{
				_isTracking = value;
				OnIsTrackingChanged(value);
			}
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

		try
		{
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

		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			// WorkEntryService.StartWorkAsync handles auto-stopping any active entry
			var result = await workEntryService.StartWorkAsync(ticketId, null, description, cancellationToken: cancellationToken);

			if (result.IsSuccess)
			{
				ActiveWork = result.Value;
				IsTracking = true;

				_logger.LogInformation("Work tracking started successfully: WorkEntryId={WorkEntryId}", result.Value.Id);

				// Notify that work entries have been modified so UI can refresh
				OnWorkEntriesModified();
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
	}

	public async Task<Result> StopTrackingAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		if (!IsTracking)
		{
			_logger.LogDebug("No active work to stop");
			return Result.Success();
		}

		_logger.LogInformation("Stopping work tracking: WorkEntryId={WorkEntryId}", ActiveWork?.Id);

		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var result = await workEntryService.StopWorkAsync(cancellationToken: cancellationToken);

			if (result.IsSuccess)
			{
				ActiveWork = null;
				IsTracking = false;

				_logger.LogInformation("Work tracking stopped successfully");

				// Notify that work entries have been modified so UI can refresh
				OnWorkEntriesModified();
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
	}

	public async Task RefreshFromDatabaseAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogDebug("Refreshing state from database");

		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var activeWork = await workEntryService.GetActiveWorkAsync(cancellationToken);

			// Update state - this will trigger events if values changed
			ActiveWork = activeWork;
			IsTracking = activeWork != null;

			_logger.LogDebug("State refreshed: IsTracking={IsTracking}", IsTracking);
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to refresh state from database");
			throw;
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

		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			// Create the entry
			var result = await workEntryService.StartWorkAsync(ticketId, startTime, description, endTime, cancellationToken);

			if (result.IsSuccess)
			{
				_logger.LogInformation("Work entry created successfully: WorkEntryId={WorkEntryId}", result.Value.Id);

				// Refresh state and notify
				await RefreshFromDatabaseAsync(cancellationToken);
				OnWorkEntriesModified();
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

		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			// Update the entry
			var result = await workEntryService.UpdateWorkEntryAsync(id, ticketId, startTime, endTime, description, cancellationToken);

			if (result.IsSuccess)
			{
				_logger.LogInformation("Work entry updated successfully: WorkEntryId={WorkEntryId}", id);

				// Refresh state and notify
				await RefreshFromDatabaseAsync(cancellationToken);
				OnWorkEntriesModified();
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
	}

	public async Task<Result> DeleteWorkEntryAsync(int id, CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogInformation("Deleting work entry: Id={Id}", id);

		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			// Delete the entry
			var result = await workEntryService.DeleteWorkEntryAsync(id, cancellationToken);

			if (result.IsSuccess)
			{
				_logger.LogInformation("Work entry deleted successfully: WorkEntryId={WorkEntryId}", id);

				// Refresh state and notify
				await RefreshFromDatabaseAsync(cancellationToken);
				OnWorkEntriesModified();
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
	}

	public async Task NotifyWorkEntriesModifiedAsync(CancellationToken cancellationToken)
	{
		ThrowIfNotInitialized();

		_logger.LogInformation("Work entries modified notification received (external)");

		try
		{
			await RefreshFromDatabaseAsync(cancellationToken);

			// Raise event only after successful refresh so listeners see consistent data
			OnWorkEntriesModified();
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to refresh state after work entries modification");
		}
	}

	#endregion State Operations

	#region Event Raising

	private void OnActiveWorkChanged(WorkEntry? activeWork)
	{
		_logger.LogDebug("Raising ActiveWorkChanged event: ActiveWorkId={ActiveWorkId}", activeWork?.Id);
		ActiveWorkChanged?.Invoke(this, activeWork);
	}

	private void OnIsTrackingChanged(bool isTracking)
	{
		_logger.LogDebug("Raising IsTrackingChanged event: IsTracking={IsTracking}", isTracking);
		IsTrackingChanged?.Invoke(this, isTracking);
	}

	private void OnWorkEntriesModified()
	{
		_logger.LogDebug("Raising WorkEntriesModified event");
		WorkEntriesModified?.Invoke(this, EventArgs.Empty);
	}

	#endregion Event Raising

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
}
