using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.Interfaces;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Application.Services;

public class WorkEntryService : IWorkEntryService
{
	private readonly IWorkEntryRepository _repository;
	private readonly ILogger<WorkEntryService> _logger;

	public WorkEntryService(IWorkEntryRepository repository, ILogger<WorkEntryService> logger)
	{
		_repository = repository;
		_logger = logger;
	}

	public async Task<Result<WorkEntry>> StartWorkAsync(string? ticketId, DateTime? startTime = null, string? description = null, DateTime? endTime = null)
	{
		_logger.LogInformation("Starting work on ticket {TicketId} with description {Description}", ticketId, description);

		// Only auto-stop previous work if we're creating an active entry (no endTime)
		if (!endTime.HasValue)
		{
			// Check if there's already an active work entry
			var activeEntry = await _repository.GetActiveWorkEntryAsync();
			if (activeEntry != null)
			{
				// Automatically stop the previous work entry
				_logger.LogInformation("Auto-stopping previous work on ticket {PreviousTicketId}", activeEntry.TicketId);

				var stopTime = DateTimeHelper.RoundToMinute(startTime ?? DateTime.Now);
				activeEntry.EndTime = stopTime;
				activeEntry.IsActive = false;
				activeEntry.UpdatedAt = DateTimeHelper.RoundToMinute(DateTime.Now);

				await _repository.UpdateAsync(activeEntry);
				_logger.LogInformation("Previous work stopped automatically");
			}
		}

		var workEntry = new WorkEntry
		{
			TicketId = ticketId,
			StartTime = DateTimeHelper.RoundToMinute(startTime ?? DateTime.Now),
			EndTime = DateTimeHelper.RoundToMinute(endTime),
			Description = description,
			IsActive = !endTime.HasValue,
			CreatedAt = DateTimeHelper.RoundToMinute(DateTime.Now)
		};

		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data");
			return Result.Failure<WorkEntry>("Invalid work entry data. Both ticket ID and description cannot be empty.");
		}

		// Check for overlaps
		if (await _repository.HasOverlappingEntriesAsync(workEntry))
		{
			_logger.LogWarning("Work entry overlaps with existing entry");
			return Result.Failure<WorkEntry>("This work entry overlaps with an existing entry. Please check your times.");
		}

		var result = await _repository.AddAsync(workEntry);
		_logger.LogInformation("Work started successfully with ID {Id}", result.Id);

		return Result.Success(result);
	}

	public async Task<Result<WorkEntry>> StopWorkAsync(DateTime? endTime = null)
	{
		_logger.LogInformation("Stopping active work");

		var activeEntry = await _repository.GetActiveWorkEntryAsync();
		if (activeEntry == null)
		{
			_logger.LogWarning("No active work entry found to stop");
			return Result.Failure<WorkEntry>("No active work entry found to stop");
		}

		activeEntry.EndTime = DateTimeHelper.RoundToMinute(endTime ?? DateTime.Now);
		activeEntry.IsActive = false;
		activeEntry.UpdatedAt = DateTimeHelper.RoundToMinute(DateTime.Now);

		if (!activeEntry.IsValid())
		{
			_logger.LogWarning("Invalid end time - must be after start time");
			return Result.Failure<WorkEntry>("Invalid end time - must be after start time");
		}

		// Check for overlaps with the new end time
		if (await _repository.HasOverlappingEntriesAsync(activeEntry))
		{
			_logger.LogWarning("Work entry would overlap with existing entry");
			return Result.Failure<WorkEntry>("This work entry would overlap with an existing entry. Please check your times.");
		}

		await _repository.UpdateAsync(activeEntry);
		_logger.LogInformation("Work stopped successfully for ticket {TicketId}", activeEntry.TicketId);

		return Result.Success(activeEntry);
	}

	public async Task<WorkEntry?> GetActiveWorkAsync()
	{
		return await _repository.GetActiveWorkEntryAsync();
	}

	public async Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateAsync(DateTime date)
	{
		return await _repository.GetByDateAsync(date);
	}

	public async Task<Result<WorkEntry>> UpdateWorkEntryAsync(int id, string? ticketId = null,
		DateTime? startTime = null, DateTime? endTime = null, string? description = null)
	{
		_logger.LogInformation("Updating work entry {Id}", id);

		var workEntry = await _repository.GetByIdAsync(id);
		if (workEntry == null)
		{
			_logger.LogWarning("Work entry with ID {Id} not found", id);
			return Result.Failure<WorkEntry>($"Work entry with ID {id} not found");
		}

		// Always update all fields (allows setting values to null)
		workEntry.TicketId = ticketId;

		if (startTime.HasValue)
		{
			workEntry.StartTime = DateTimeHelper.RoundToMinute(startTime.Value);
		}

		// Always update endTime (allows clearing it by setting to null)
		workEntry.EndTime = DateTimeHelper.RoundToMinute(endTime);
		workEntry.IsActive = !endTime.HasValue;

		workEntry.Description = description;
		workEntry.UpdatedAt = DateTimeHelper.RoundToMinute(DateTime.Now);

		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data after update");
			return Result.Failure<WorkEntry>("Invalid work entry data after update. Both ticket ID and description cannot be empty, and end time must be after start time.");
		}

		// Check for overlaps
		if (await _repository.HasOverlappingEntriesAsync(workEntry))
		{
			_logger.LogWarning("Work entry would overlap with existing entry");
			return Result.Failure<WorkEntry>("This work entry would overlap with an existing entry. Please check your times.");
		}

		await _repository.UpdateAsync(workEntry);
		_logger.LogInformation("Work entry updated successfully");

		return Result.Success(workEntry);
	}

	public async Task<Result> DeleteWorkEntryAsync(int id)
	{
		_logger.LogInformation("Deleting work entry {Id}", id);

		// Check if entry exists before deleting
		var workEntry = await _repository.GetByIdAsync(id);
		if (workEntry == null)
		{
			_logger.LogWarning("Work entry with ID {Id} not found", id);
			return Result.Failure($"Work entry with ID {id} not found");
		}

		await _repository.DeleteAsync(id);
		_logger.LogInformation("Work entry {Id} deleted successfully", id);

		return Result.Success();
	}

	public async Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateRangeAsync(DateTime startDate, DateTime endDate)
	{
		return await _repository.GetByDateRangeAsync(startDate, endDate);
	}
}