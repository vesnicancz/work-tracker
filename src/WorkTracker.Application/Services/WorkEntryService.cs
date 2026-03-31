using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.Models;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Interfaces;

namespace WorkTracker.Application.Services;

public sealed class WorkEntryService : IWorkEntryService
{
	private readonly IWorkEntryRepository _repository;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<WorkEntryService> _logger;

	public WorkEntryService(IWorkEntryRepository repository, TimeProvider timeProvider, ILogger<WorkEntryService> logger)
	{
		_repository = repository;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	private DateTime Now => _timeProvider.GetLocalNow().DateTime;

	public async Task<Result<WorkEntry>> StartWorkAsync(string? ticketId, DateTime? startTime = null, string? description = null, DateTime? endTime = null, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Starting work on ticket {TicketId} with description {Description}", ticketId, description);

		var now = Now;

		// Only auto-stop previous work if we're creating an active entry (no endTime)
		if (!endTime.HasValue)
		{
			// Check if there's already an active work entry
			var activeEntry = await _repository.GetActiveWorkEntryAsync(cancellationToken);
			if (activeEntry != null)
			{
				// Automatically stop the previous work entry
				_logger.LogInformation("Auto-stopping previous work on ticket {PreviousTicketId}", activeEntry.TicketId);

				activeEntry.Stop(DateTimeHelper.RoundToMinute(startTime ?? now), DateTimeHelper.RoundToMinute(now));

				await _repository.UpdateAsync(activeEntry, cancellationToken);
				_logger.LogInformation("Previous work stopped automatically");
			}
		}

		var workEntry = WorkEntry.Create(
			ticketId,
			DateTimeHelper.RoundToMinute(startTime ?? now),
			DateTimeHelper.RoundToMinute(endTime),
			description,
			DateTimeHelper.RoundToMinute(now));

		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data");
			return Result.Failure<WorkEntry>("Invalid work entry data. Both ticket ID and description cannot be empty.");
		}

		// Check for overlaps
		if (await _repository.HasOverlappingEntriesAsync(workEntry, cancellationToken))
		{
			_logger.LogWarning("Work entry overlaps with existing entry");
			return Result.Failure<WorkEntry>("This work entry overlaps with an existing entry. Please check your times.");
		}

		var result = await _repository.AddAsync(workEntry, cancellationToken);
		_logger.LogInformation("Work started successfully with ID {Id}", result.Id);

		return Result.Success(result);
	}

	public async Task<Result<WorkEntry>> StopWorkAsync(DateTime? endTime = null, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Stopping active work");

		var activeEntry = await _repository.GetActiveWorkEntryAsync(cancellationToken);
		if (activeEntry == null)
		{
			_logger.LogWarning("No active work entry found to stop");
			return Result.Failure<WorkEntry>("No active work entry found to stop");
		}

		var now = Now;
		activeEntry.Stop(DateTimeHelper.RoundToMinute(endTime ?? now), DateTimeHelper.RoundToMinute(now));

		if (!activeEntry.IsValid())
		{
			_logger.LogWarning("Invalid end time - must be after start time");
			return Result.Failure<WorkEntry>("Invalid end time - must be after start time");
		}

		// Check for overlaps with the new end time
		if (await _repository.HasOverlappingEntriesAsync(activeEntry, cancellationToken))
		{
			_logger.LogWarning("Work entry would overlap with existing entry");
			return Result.Failure<WorkEntry>("This work entry would overlap with an existing entry. Please check your times.");
		}

		await _repository.UpdateAsync(activeEntry, cancellationToken);
		_logger.LogInformation("Work stopped successfully for ticket {TicketId}", activeEntry.TicketId);

		return Result.Success(activeEntry);
	}

	public async Task<WorkEntry?> GetActiveWorkAsync(CancellationToken cancellationToken)
	{
		return await _repository.GetActiveWorkEntryAsync(cancellationToken);
	}

	public async Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateAsync(DateTime date, CancellationToken cancellationToken)
	{
		return await _repository.GetByDateAsync(date, cancellationToken);
	}

	public async Task<Result<WorkEntry>> UpdateWorkEntryAsync(int id, string? ticketId = null,
		DateTime? startTime = null, DateTime? endTime = null, string? description = null, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Updating work entry {Id}", id);

		var workEntry = await _repository.GetByIdAsync(id, cancellationToken);
		if (workEntry == null)
		{
			_logger.LogWarning("Work entry with ID {Id} not found", id);
			return Result.Failure<WorkEntry>($"Work entry with ID {id} not found");
		}

		// Always update all fields (allows setting values to null)
		workEntry.UpdateFields(
			ticketId,
			startTime.HasValue ? DateTimeHelper.RoundToMinute(startTime.Value) : null,
			DateTimeHelper.RoundToMinute(endTime),
			description,
			DateTimeHelper.RoundToMinute(Now));

		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data after update");
			return Result.Failure<WorkEntry>("Invalid work entry data after update. Both ticket ID and description cannot be empty, and end time must be after start time.");
		}

		// Check for overlaps
		if (await _repository.HasOverlappingEntriesAsync(workEntry, cancellationToken))
		{
			_logger.LogWarning("Work entry would overlap with existing entry");
			return Result.Failure<WorkEntry>("This work entry would overlap with an existing entry. Please check your times.");
		}

		await _repository.UpdateAsync(workEntry, cancellationToken);
		_logger.LogInformation("Work entry updated successfully");

		return Result.Success(workEntry);
	}

	public async Task<Result> DeleteWorkEntryAsync(int id, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Deleting work entry {Id}", id);

		// Check if entry exists before deleting
		var workEntry = await _repository.GetByIdAsync(id, cancellationToken);
		if (workEntry == null)
		{
			_logger.LogWarning("Work entry with ID {Id} not found", id);
			return Result.Failure($"Work entry with ID {id} not found");
		}

		await _repository.DeleteAsync(id, cancellationToken);
		_logger.LogInformation("Work entry {Id} deleted successfully", id);

		return Result.Success();
	}

	public async Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		return await _repository.GetByDateRangeAsync(startDate, endDate, cancellationToken);
	}

	public async Task<OverlapResolutionPlan> ComputeOverlapResolutionAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		var roundedStart = DateTimeHelper.RoundToMinute(startTime);
		var roundedEnd = DateTimeHelper.RoundToMinute(endTime);

		var overlapping = await _repository.GetOverlappingEntriesAsync(excludeEntryId, roundedStart, roundedEnd, cancellationToken);

		if (overlapping.Count == 0)
		{
			return new OverlapResolutionPlan();
		}

		var candidateEnd = roundedEnd ?? DateTime.MaxValue;
		var adjustments = new List<OverlapAdjustment>();

		foreach (var existing in overlapping)
		{
			var existingEnd = existing.EndTime ?? DateTime.MaxValue;
			var adjustment = DetermineAdjustment(existing, existingEnd, roundedStart, candidateEnd);
			adjustments.Add(adjustment);
		}

		return new OverlapResolutionPlan { Adjustments = adjustments };
	}

	private static OverlapAdjustment DetermineAdjustment(WorkEntry existing, DateTime existingEnd, DateTime candidateStart, DateTime candidateEnd)
	{
		var minDuration = TimeSpan.FromMinutes(1);

		// Complete cover: candidate fully contains existing
		if (candidateStart <= existing.StartTime && candidateEnd >= existingEnd)
		{
			return new OverlapAdjustment(
				existing.Id, existing.TicketId, existing.Description,
				OverlapAdjustmentKind.Delete,
				existing.StartTime, existing.EndTime,
				null, null);
		}

		// Split: candidate is inside existing (only for completed entries)
		// Active entries (no EndTime) are never split — they are trimmed instead,
		// because splitting would create a new active entry after the candidate.
		if (candidateStart > existing.StartTime && candidateEnd < existingEnd && existing.EndTime.HasValue)
		{
			// Check if either half would be too short
			var firstHalf = candidateStart - existing.StartTime;
			var secondHalf = existingEnd - candidateEnd;

			if (firstHalf < minDuration && secondHalf < minDuration)
			{
				return new OverlapAdjustment(
					existing.Id, existing.TicketId, existing.Description,
					OverlapAdjustmentKind.Delete,
					existing.StartTime, existing.EndTime,
					null, null);
			}

			if (firstHalf < minDuration)
			{
				return new OverlapAdjustment(
					existing.Id, existing.TicketId, existing.Description,
					OverlapAdjustmentKind.TrimStart,
					existing.StartTime, existing.EndTime,
					candidateEnd, null);
			}

			if (secondHalf < minDuration)
			{
				return new OverlapAdjustment(
					existing.Id, existing.TicketId, existing.Description,
					OverlapAdjustmentKind.TrimEnd,
					existing.StartTime, existing.EndTime,
					null, candidateStart);
			}

			return new OverlapAdjustment(
				existing.Id, existing.TicketId, existing.Description,
				OverlapAdjustmentKind.Split,
				existing.StartTime, existing.EndTime,
				candidateEnd, candidateStart);
		}

		// Head overlap: candidate covers the end of existing
		if (candidateStart > existing.StartTime)
		{
			var remaining = candidateStart - existing.StartTime;
			if (remaining < minDuration)
			{
				return new OverlapAdjustment(
					existing.Id, existing.TicketId, existing.Description,
					OverlapAdjustmentKind.Delete,
					existing.StartTime, existing.EndTime,
					null, null);
			}

			return new OverlapAdjustment(
				existing.Id, existing.TicketId, existing.Description,
				OverlapAdjustmentKind.TrimEnd,
				existing.StartTime, existing.EndTime,
				null, candidateStart);
		}

		// Tail overlap: candidate covers the beginning of existing
		{
			var remaining = existingEnd - candidateEnd;
			if (remaining < minDuration)
			{
				return new OverlapAdjustment(
					existing.Id, existing.TicketId, existing.Description,
					OverlapAdjustmentKind.Delete,
					existing.StartTime, existing.EndTime,
					null, null);
			}

			return new OverlapAdjustment(
				existing.Id, existing.TicketId, existing.Description,
				OverlapAdjustmentKind.TrimStart,
				existing.StartTime, existing.EndTime,
				candidateEnd, null);
		}
	}

	public async Task<Result<WorkEntry>> CreateWithOverlapResolutionAsync(
		string? ticketId, DateTime startTime, string? description, DateTime? endTime,
		OverlapResolutionPlan plan, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Creating work entry with overlap resolution ({AdjustmentCount} adjustments)", plan.Adjustments.Count);

		var now = Now;
		var roundedStart = DateTimeHelper.RoundToMinute(startTime);
		var roundedEnd = DateTimeHelper.RoundToMinute(endTime);

		// Validate the candidate entry before applying any adjustments to avoid partial state changes
		var workEntry = WorkEntry.Create(ticketId, roundedStart, roundedEnd, description, DateTimeHelper.RoundToMinute(now));

		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data");
			return Result.Failure<WorkEntry>("Invalid work entry data. Both ticket ID and description cannot be empty.");
		}

		// Apply adjustments to overlapping entries
		var applyResult = await ApplyAdjustmentsAsync(plan, now, cancellationToken);
		if (applyResult.IsFailure)
		{
			return Result.Failure<WorkEntry>(applyResult.Error);
		}

		// Create the new entry (skip overlap check since we resolved them)
		var result = await _repository.AddAsync(workEntry, cancellationToken);
		_logger.LogInformation("Work entry created successfully with overlap resolution, ID {Id}", result.Id);

		return Result.Success(result);
	}

	public async Task<Result<WorkEntry>> UpdateWithOverlapResolutionAsync(
		int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description,
		OverlapResolutionPlan plan, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Updating work entry {Id} with overlap resolution ({AdjustmentCount} adjustments)", id, plan.Adjustments.Count);

		var now = Now;
		var workEntry = await _repository.GetByIdAsync(id, cancellationToken);
		if (workEntry == null)
		{
			return Result.Failure<WorkEntry>($"Work entry with ID {id} not found");
		}

		// Validate the updated fields before applying any adjustments to avoid partial state changes
		workEntry.UpdateFields(
			ticketId,
			DateTimeHelper.RoundToMinute(startTime),
			DateTimeHelper.RoundToMinute(endTime),
			description,
			DateTimeHelper.RoundToMinute(now));

		if (!workEntry.IsValid())
		{
			return Result.Failure<WorkEntry>("Invalid work entry data after update. Both ticket ID and description cannot be empty, and end time must be after start time.");
		}

		// Apply adjustments to overlapping entries
		var applyResult = await ApplyAdjustmentsAsync(plan, now, cancellationToken);
		if (applyResult.IsFailure)
		{
			return Result.Failure<WorkEntry>(applyResult.Error);
		}

		// Save the updated entry (skip overlap check since we resolved them)
		await _repository.UpdateAsync(workEntry, cancellationToken);
		_logger.LogInformation("Work entry {Id} updated successfully with overlap resolution", id);

		return Result.Success(workEntry);
	}

	private async Task<Result> ApplyAdjustmentsAsync(OverlapResolutionPlan plan, DateTime now, CancellationToken cancellationToken)
	{
		var roundedNow = DateTimeHelper.RoundToMinute(now);

		foreach (var adjustment in plan.Adjustments)
		{
			switch (adjustment.Kind)
			{
				case OverlapAdjustmentKind.Delete:
					await _repository.DeleteAsync(adjustment.WorkEntryId, cancellationToken);
					_logger.LogInformation("Deleted overlapping entry {Id}", adjustment.WorkEntryId);
					break;

				case OverlapAdjustmentKind.TrimEnd:
				{
					if (!adjustment.NewEnd.HasValue)
					{
						return Result.Failure($"TrimEnd adjustment for entry {adjustment.WorkEntryId} is missing NewEnd value");
					}

					var entry = await _repository.GetByIdAsync(adjustment.WorkEntryId, cancellationToken);
					if (entry == null)
					{
						return Result.Failure($"Overlapping entry {adjustment.WorkEntryId} no longer exists");
					}

					entry.AdjustEndTime(adjustment.NewEnd.Value, roundedNow);
					await _repository.UpdateAsync(entry, cancellationToken);
					_logger.LogInformation("Trimmed end of entry {Id} to {NewEnd}", adjustment.WorkEntryId, adjustment.NewEnd);
					break;
				}

				case OverlapAdjustmentKind.TrimStart:
				{
					if (!adjustment.NewStart.HasValue)
					{
						return Result.Failure($"TrimStart adjustment for entry {adjustment.WorkEntryId} is missing NewStart value");
					}

					var entry = await _repository.GetByIdAsync(adjustment.WorkEntryId, cancellationToken);
					if (entry == null)
					{
						return Result.Failure($"Overlapping entry {adjustment.WorkEntryId} no longer exists");
					}

					entry.AdjustStartTime(adjustment.NewStart.Value, roundedNow);
					await _repository.UpdateAsync(entry, cancellationToken);
					_logger.LogInformation("Trimmed start of entry {Id} to {NewStart}", adjustment.WorkEntryId, adjustment.NewStart);
					break;
				}

				case OverlapAdjustmentKind.Split:
				{
					if (!adjustment.NewEnd.HasValue || !adjustment.NewStart.HasValue)
					{
						return Result.Failure($"Split adjustment for entry {adjustment.WorkEntryId} is missing NewEnd or NewStart value");
					}

					var entry = await _repository.GetByIdAsync(adjustment.WorkEntryId, cancellationToken);
					if (entry == null)
					{
						return Result.Failure($"Overlapping entry {adjustment.WorkEntryId} no longer exists");
					}

					// First half: original start to candidate start (NewEnd)
					entry.AdjustEndTime(adjustment.NewEnd.Value, roundedNow);
					await _repository.UpdateAsync(entry, cancellationToken);

					// Second half: candidate end (NewStart) to original end
					var secondHalf = WorkEntry.Create(
						entry.TicketId,
						adjustment.NewStart.Value,
						adjustment.OriginalEnd,
						entry.Description,
						roundedNow);
					await _repository.AddAsync(secondHalf, cancellationToken);

					_logger.LogInformation("Split entry {Id} into two parts", adjustment.WorkEntryId);
					break;
				}
			}
		}

		return Result.Success();
	}
}
