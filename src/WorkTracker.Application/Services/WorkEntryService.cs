using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.Interfaces;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Domain.Services;

namespace WorkTracker.Application.Services;

public sealed class WorkEntryService : IWorkEntryService
{

	private readonly IWorkEntryRepository _repository;
	private readonly IUnitOfWorkFactory _unitOfWorkFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<WorkEntryService> _logger;

	public WorkEntryService(IWorkEntryRepository repository, IUnitOfWorkFactory unitOfWorkFactory, TimeProvider timeProvider, ILogger<WorkEntryService> logger)
	{
		_repository = repository;
		_unitOfWorkFactory = unitOfWorkFactory;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	private DateTime Now => _timeProvider.GetLocalNow().DateTime;

	public async Task<Result<WorkEntry>> StartWorkAsync(string? ticketId, DateTime? startTime = null, string? description = null, DateTime? endTime = null, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Starting work on ticket {TicketId} with description {Description}", ticketId, description);

		var now = Now;
		var roundedNow = DateTimeHelper.RoundToMinute(now);
		var workEntry = WorkEntry.Create(
			ticketId,
			DateTimeHelper.RoundToMinute(startTime ?? now),
			DateTimeHelper.RoundToMinute(endTime),
			description,
			roundedNow);

		if (!endTime.HasValue)
		{
			return await StartOpenEndedWorkAsync(workEntry, roundedNow, cancellationToken);
		}

		var validation = await ValidateNewEntryAsync(workEntry, cancellationToken);
		if (validation.IsFailure)
		{
			return validation;
		}

		var result = await _repository.AddAsync(workEntry, cancellationToken);
		_logger.LogInformation("Work started successfully with ID {Id}", result.Id);

		return Result.Success(result);
	}

	/// <summary>
	/// Starts an entry without an end time. Such an entry only claims its start instant, so entries
	/// planned for later (e.g. an upcoming meeting) don't block it - they are checked again once it
	/// gets stopped. Whatever is still running at that instant, the active entry as well as a meeting
	/// with a set end time, gets closed there, in the same transaction as the new entry.
	/// </summary>
	private async Task<Result<WorkEntry>> StartOpenEndedWorkAsync(WorkEntry workEntry, DateTime roundedNow, CancellationToken cancellationToken)
	{
		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data for ticket {TicketId} ({StartTime} - {EndTime})", workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEntry);
		}

		var overlapping = await _repository.GetOverlappingEntriesAsync(null, workEntry.StartTime, null, cancellationToken);

		// Closing an entry at the new start only works for one that began before it. One starting at
		// the same minute would shrink to nothing, and an open-ended one starting later can't be closed
		// before it began.
		if (overlapping.Any(e => e.StartTime >= workEntry.StartTime && (e.EndTime == null || e.StartTime == workEntry.StartTime)))
		{
			_logger.LogWarning("Work entry overlaps with existing entry for ticket {TicketId} ({StartTime} - {EndTime})", workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.Overlap);
		}

		var running = overlapping.Where(e => e.StartTime < workEntry.StartTime).ToList();
		if (running.Count == 0)
		{
			var added = await _repository.AddAsync(workEntry, cancellationToken);
			_logger.LogInformation("Work started successfully with ID {Id}", added.Id);
			return Result.Success(added);
		}

		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		foreach (var entry in running)
		{
			_logger.LogInformation("Auto-stopping previous work {Id} on ticket {PreviousTicketId}", entry.Id, entry.TicketId);
			entry.Stop(workEntry.StartTime, roundedNow);
			await uow.WorkEntries.UpdateAsync(entry, cancellationToken);
		}

		var result = await uow.WorkEntries.AddAsync(workEntry, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);

		_logger.LogInformation("Previous work stopped and new work started successfully with ID {Id}", result.Id);
		return Result.Success(result);
	}

	/// <summary>
	/// Validates a freshly-created entry with an end time (structural + overlap check).
	/// </summary>
	private async Task<Result<WorkEntry>> ValidateNewEntryAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data for ticket {TicketId} ({StartTime} - {EndTime})", workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEntry);
		}

		if (await _repository.HasOverlappingEntriesAsync(null, workEntry.StartTime, workEntry.EndTime, cancellationToken))
		{
			_logger.LogWarning("Work entry overlaps with existing entry for ticket {TicketId} ({StartTime} - {EndTime})", workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.Overlap);
		}

		return Result.Success(workEntry);
	}

	public async Task<Result<WorkEntry>> StopWorkAsync(DateTime? endTime = null, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Stopping active work");

		var activeEntry = await _repository.GetActiveWorkEntryAsync(cancellationToken);
		if (activeEntry == null)
		{
			_logger.LogWarning("No active work entry found to stop");
			return Result.Failure<WorkEntry>(WorkEntryErrors.NoActiveWork);
		}

		var now = Now;
		activeEntry.Stop(DateTimeHelper.RoundToMinute(endTime ?? now), DateTimeHelper.RoundToMinute(now));

		if (!activeEntry.IsValid())
		{
			_logger.LogWarning("Invalid end time for ticket {TicketId} - start time {StartTime}, attempted end time {EndTime}", activeEntry.TicketId, activeEntry.StartTime, activeEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEndTime);
		}

		// Check for overlaps with the new end time
		if (await _repository.HasOverlappingEntriesAsync(activeEntry, cancellationToken))
		{
			_logger.LogWarning("Stopped work entry for ticket {TicketId} would overlap with existing entry ({StartTime} - {EndTime})", activeEntry.TicketId, activeEntry.StartTime, activeEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.Overlap);
		}

		await _repository.UpdateAsync(activeEntry, cancellationToken);
		_logger.LogInformation("Work stopped successfully for ticket {TicketId}", activeEntry.TicketId);

		return Result.Success(activeEntry);
	}

	public async Task<WorkEntry?> GetActiveWorkAsync(CancellationToken cancellationToken)
	{
		return await _repository.GetActiveWorkEntryAsync(cancellationToken);
	}

	public async Task<WorkEntry?> GetWorkEntryByIdAsync(int id, CancellationToken cancellationToken)
	{
		return await _repository.GetByIdAsync(id, cancellationToken);
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
			return Result.Failure<WorkEntry>(WorkEntryErrors.NotFound(id));
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
			_logger.LogWarning("Invalid work entry data after update for entry {Id}, ticket {TicketId} ({StartTime} - {EndTime})", id, ticketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEntryAfterUpdate);
		}

		// Check for overlaps
		if (await _repository.HasOverlappingEntriesAsync(workEntry, cancellationToken))
		{
			_logger.LogWarning("Work entry {Id} would overlap with existing entry ({StartTime} - {EndTime})", id, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.Overlap);
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
			return Result.Failure(WorkEntryErrors.NotFound(id));
		}

		await _repository.DeleteAsync(id, cancellationToken);
		_logger.LogInformation("Work entry {Id} deleted successfully", id);

		return Result.Success();
	}

	public async Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		return await _repository.GetByDateRangeAsync(startDate, endDate, cancellationToken);
	}

	public Task<OverlapResolutionPlan> ComputeOverlapResolutionAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		return BuildOverlapPlanAsync(_repository, excludeEntryId, DateTimeHelper.RoundToMinute(startTime), DateTimeHelper.RoundToMinute(endTime), cancellationToken);
	}

	private static async Task<OverlapResolutionPlan> BuildOverlapPlanAsync(IWorkEntryRepository repository, int? excludeEntryId, DateTime roundedStart, DateTime? roundedEnd, CancellationToken cancellationToken)
	{
		// For the query: when endTime is null (active/ongoing entry), use +1 minute
		// to detect overlaps at the exact start time (predicate uses StartTime < end)
		// while avoiding matching all future entries
		var queryEnd = roundedEnd ?? roundedStart.AddMinutes(1);

		var overlapping = await repository.GetOverlappingEntriesAsync(excludeEntryId, roundedStart, queryEnd, cancellationToken);

		if (overlapping.Count == 0)
		{
			return new OverlapResolutionPlan();
		}

		// For adjustments: use DateTime.MaxValue for active entries so the resolver
		// correctly produces TrimEnd/Delete (not TrimStart/Split with an artificial +1min boundary)
		var candidateEnd = roundedEnd ?? DateTime.MaxValue;
		var adjustments = OverlapResolver.Resolve(overlapping, roundedStart, candidateEnd);

		return new OverlapResolutionPlan { Adjustments = adjustments };
	}

	/// <summary>
	/// A plan is computed before the user confirms it, and the entries can change while the question is
	/// open: the CLI, a remote controller or the Pomodoro timer don't wait for the dialog. Applying it
	/// then would delete or trim entries by what they used to be, so the transaction checks it first.
	/// </summary>
	private async Task<bool> IsPlanStillValidAsync(IWorkEntryRepository repository, OverlapResolutionPlan plan, int? excludeEntryId, DateTime roundedStart, DateTime? roundedEnd, CancellationToken cancellationToken)
	{
		var currentPlan = await BuildOverlapPlanAsync(repository, excludeEntryId, roundedStart, roundedEnd, cancellationToken);
		if (currentPlan.Adjustments.SequenceEqual(plan.Adjustments))
		{
			return true;
		}

		_logger.LogWarning("Overlap resolution plan is out of date ({StartTime} - {EndTime}), nothing was changed", roundedStart, roundedEnd);
		return false;
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
			_logger.LogWarning("Invalid work entry data for ticket {TicketId} ({StartTime} - {EndTime})", ticketId, roundedStart, roundedEnd);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEntry);
		}

		// All adjustments + new entry in a single transaction
		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		if (!await IsPlanStillValidAsync(uow.WorkEntries, plan, null, roundedStart, roundedEnd, cancellationToken))
		{
			return Result.Failure<WorkEntry>(WorkEntryErrors.PlanOutdated);
		}

		var applyResult = await ApplyAdjustmentsAsync(uow.WorkEntries, plan, now, cancellationToken);
		if (applyResult.IsFailure)
		{
			return Result.Failure<WorkEntry>(applyResult.Error);
		}

		var result = await uow.WorkEntries.AddAsync(workEntry, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);

		_logger.LogInformation("Work entry created successfully with overlap resolution, ID {Id}", result.Id);
		return Result.Success(result);
	}

	public async Task<Result<WorkEntry>> UpdateWithOverlapResolutionAsync(
		int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description,
		OverlapResolutionPlan plan, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Updating work entry {Id} with overlap resolution ({AdjustmentCount} adjustments)", id, plan.Adjustments.Count);

		var now = Now;

		// UoW is opened before validation because we need GetByIdAsync to load the tracked entity
		// within the transactional scope — validation runs against the updated state below.
		// If validation or adjustment application fails, disposing the UoW rolls everything back.
		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		var workEntry = await uow.WorkEntries.GetByIdAsync(id, cancellationToken);
		if (workEntry == null)
		{
			return Result.Failure<WorkEntry>(WorkEntryErrors.NotFound(id));
		}

		var roundedStart = DateTimeHelper.RoundToMinute(startTime);
		var roundedEnd = DateTimeHelper.RoundToMinute(endTime);

		// Validate the updated fields before applying any adjustments to avoid partial state changes
		workEntry.UpdateFields(
			ticketId,
			roundedStart,
			roundedEnd,
			description,
			DateTimeHelper.RoundToMinute(now));

		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data after update with resolution for entry {Id}, ticket {TicketId} ({StartTime} - {EndTime})", id, ticketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEntryAfterUpdate);
		}

		if (!await IsPlanStillValidAsync(uow.WorkEntries, plan, id, roundedStart, roundedEnd, cancellationToken))
		{
			return Result.Failure<WorkEntry>(WorkEntryErrors.PlanOutdated);
		}

		// Apply adjustments to overlapping entries
		var applyResult = await ApplyAdjustmentsAsync(uow.WorkEntries, plan, now, cancellationToken);
		if (applyResult.IsFailure)
		{
			return Result.Failure<WorkEntry>(applyResult.Error);
		}

		// Save the updated entry (skip overlap check since we resolved them)
		await uow.WorkEntries.UpdateAsync(workEntry, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);

		_logger.LogInformation("Work entry {Id} updated successfully with overlap resolution", id);
		return Result.Success(workEntry);
	}

	private async Task<Result> ApplyAdjustmentsAsync(IWorkEntryRepository repository, OverlapResolutionPlan plan, DateTime now, CancellationToken cancellationToken)
	{
		var roundedNow = DateTimeHelper.RoundToMinute(now);

		foreach (var adjustment in plan.Adjustments)
		{
			switch (adjustment.Kind)
			{
				case OverlapAdjustmentKind.Delete:
					await repository.DeleteAsync(adjustment.WorkEntryId, cancellationToken);
					_logger.LogInformation("Deleted overlapping entry {Id}", adjustment.WorkEntryId);
					break;

				case OverlapAdjustmentKind.TrimEnd:
				{
					if (!adjustment.NewEnd.HasValue)
					{
						return Result.Failure($"TrimEnd adjustment for entry {adjustment.WorkEntryId} is missing NewEnd value");
					}

					var entry = await repository.GetByIdAsync(adjustment.WorkEntryId, cancellationToken);
					if (entry == null)
					{
						return Result.Failure($"Overlapping entry {adjustment.WorkEntryId} no longer exists");
					}

					entry.AdjustEndTime(adjustment.NewEnd.Value, roundedNow);
					await repository.UpdateAsync(entry, cancellationToken);
					_logger.LogInformation("Trimmed end of entry {Id} to {NewEnd}", adjustment.WorkEntryId, adjustment.NewEnd);
					break;
				}

				case OverlapAdjustmentKind.TrimStart:
				{
					if (!adjustment.NewStart.HasValue)
					{
						return Result.Failure($"TrimStart adjustment for entry {adjustment.WorkEntryId} is missing NewStart value");
					}

					var entry = await repository.GetByIdAsync(adjustment.WorkEntryId, cancellationToken);
					if (entry == null)
					{
						return Result.Failure($"Overlapping entry {adjustment.WorkEntryId} no longer exists");
					}

					entry.AdjustStartTime(adjustment.NewStart.Value, roundedNow);
					await repository.UpdateAsync(entry, cancellationToken);
					_logger.LogInformation("Trimmed start of entry {Id} to {NewStart}", adjustment.WorkEntryId, adjustment.NewStart);
					break;
				}

				case OverlapAdjustmentKind.Split:
				{
					if (!adjustment.NewEnd.HasValue || !adjustment.NewStart.HasValue)
					{
						return Result.Failure($"Split adjustment for entry {adjustment.WorkEntryId} is missing NewEnd or NewStart value");
					}

					var entry = await repository.GetByIdAsync(adjustment.WorkEntryId, cancellationToken);
					if (entry == null)
					{
						return Result.Failure($"Overlapping entry {adjustment.WorkEntryId} no longer exists");
					}

					// First half: original start to candidate start (NewEnd)
					entry.AdjustEndTime(adjustment.NewEnd.Value, roundedNow);
					await repository.UpdateAsync(entry, cancellationToken);

					// Second half: candidate end (NewStart) to original end
					var secondHalf = WorkEntry.Create(
						entry.TicketId,
						adjustment.NewStart.Value,
						adjustment.OriginalEnd,
						entry.Description,
						roundedNow);
					await repository.AddAsync(secondHalf, cancellationToken);

					_logger.LogInformation("Split entry {Id} into two parts", adjustment.WorkEntryId);
					break;
				}
			}
		}

		return Result.Success();
	}
}
