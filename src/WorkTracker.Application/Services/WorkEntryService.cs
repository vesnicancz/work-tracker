using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Interfaces;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Domain.Services;

namespace WorkTracker.Application.Services;

/// <remarks>
/// Every write runs in a unit of work, together with the checks it depends on. Those transactions take
/// the write lock of the database up front, so the GUI and the CLI can't both pass a check and then both
/// write.
/// </remarks>
public sealed class WorkEntryService : IWorkEntryService
{
	private readonly IWorkEntryReader _reader;
	private readonly IUnitOfWorkFactory _unitOfWorkFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<WorkEntryService> _logger;

	public WorkEntryService(IWorkEntryReader reader, IUnitOfWorkFactory unitOfWorkFactory, TimeProvider timeProvider, ILogger<WorkEntryService> logger)
	{
		_reader = reader;
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

		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data for ticket {TicketId} ({StartTime} - {EndTime})", workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEntry);
		}

		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		if (await uow.WorkEntries.HasOverlappingEntriesAsync(null, workEntry.StartTime, workEntry.EndTime, cancellationToken))
		{
			_logger.LogWarning("Work entry overlaps with existing entry for ticket {TicketId} ({StartTime} - {EndTime})", workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.Overlap);
		}

		var result = await uow.WorkEntries.AddAsync(workEntry, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);
		_logger.LogInformation("Work started successfully with ID {Id}", result.Id);

		return Result.Success(result);
	}

	/// <summary>
	/// Starts an entry without an end time, ending what is still running then in the same transaction.
	/// See <see cref="OverlapResolver.ResolveOpenEndedStart"/>.
	/// </summary>
	private async Task<Result<WorkEntry>> StartOpenEndedWorkAsync(WorkEntry workEntry, DateTime roundedNow, CancellationToken cancellationToken)
	{
		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data for ticket {TicketId} ({StartTime} - {EndTime})", workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEntry);
		}

		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		var overlapping = await uow.WorkEntries.GetOverlappingEntriesAsync(null, workEntry.StartTime, null, cancellationToken);

		var adjustments = OverlapResolver.ResolveOpenEndedStart(overlapping, workEntry.StartTime);
		if (adjustments == null)
		{
			_logger.LogWarning("Work entry overlaps with existing entry for ticket {TicketId} ({StartTime} - {EndTime})", workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.Overlap);
		}

		var applyResult = await ApplyAdjustmentsAsync(uow.WorkEntries, adjustments, roundedNow, cancellationToken);
		if (applyResult.IsFailure)
		{
			return Result.Failure<WorkEntry>(applyResult);
		}

		var result = await uow.WorkEntries.AddAsync(workEntry, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);

		_logger.LogInformation("Work started successfully with ID {Id}", result.Id);
		return Result.Success(result);
	}

	public async Task<Result<WorkEntry>> StopWorkAsync(DateTime? endTime = null, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Stopping active work");

		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		var activeEntry = await uow.WorkEntries.GetActiveWorkEntryAsync(cancellationToken);
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
		if (await uow.WorkEntries.HasOverlappingEntriesAsync(activeEntry, cancellationToken))
		{
			_logger.LogWarning("Stopped work entry for ticket {TicketId} would overlap with existing entry ({StartTime} - {EndTime})", activeEntry.TicketId, activeEntry.StartTime, activeEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.Overlap);
		}

		await uow.WorkEntries.UpdateAsync(activeEntry, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);
		_logger.LogInformation("Work stopped successfully for ticket {TicketId}", activeEntry.TicketId);

		return Result.Success(activeEntry);
	}

	public async Task<WorkEntry?> GetActiveWorkAsync(CancellationToken cancellationToken)
	{
		return await _reader.GetActiveWorkEntryAsync(cancellationToken);
	}

	public async Task<WorkEntry?> GetWorkEntryByIdAsync(int id, CancellationToken cancellationToken)
	{
		return await _reader.GetByIdAsync(id, cancellationToken);
	}

	public async Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateAsync(DateTime date, CancellationToken cancellationToken)
	{
		return await _reader.GetByDateAsync(date, cancellationToken);
	}

	public Task<Result<WorkEntry>> UpdateWorkEntryAsync(int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description, CancellationToken cancellationToken)
	{
		return ModifyEntryAsync(
			id,
			(entry, now) => entry.UpdateFields(ticketId, DateTimeHelper.RoundToMinute(startTime), DateTimeHelper.RoundToMinute(endTime), description, now),
			cancellationToken);
	}

	public Task<Result<WorkEntry>> EditWorkEntryAsync(int id, WorkEntryChanges changes, CancellationToken cancellationToken)
	{
		return ModifyEntryAsync(
			id,
			(entry, now) => entry.UpdateFields(
				changes.TicketId ?? entry.TicketId,
				DateTimeHelper.RoundToMinute(changes.StartTime ?? entry.StartTime),
				DateTimeHelper.RoundToMinute(changes.EndTime ?? entry.EndTime),
				changes.Description ?? entry.Description,
				now),
			cancellationToken);
	}

	public async Task<Result<WorkEntry>> ShiftActiveWorkStartAsync(TimeSpan delta, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Shifting the start of active work by {Delta}", delta);

		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		var activeEntry = await uow.WorkEntries.GetActiveWorkEntryAsync(cancellationToken);
		if (activeEntry == null)
		{
			_logger.LogWarning("No active work entry found to shift");
			return Result.Failure<WorkEntry>(WorkEntryErrors.NoActiveWork);
		}

		activeEntry.AdjustStartTime(DateTimeHelper.RoundToMinute(activeEntry.StartTime + delta), DateTimeHelper.RoundToMinute(Now));

		return await SaveModifiedEntryAsync(uow, activeEntry, cancellationToken);
	}

	private async Task<Result<WorkEntry>> ModifyEntryAsync(int id, Action<WorkEntry, DateTime> modify, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Updating work entry {Id}", id);

		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		var workEntry = await uow.WorkEntries.GetByIdAsync(id, cancellationToken);
		if (workEntry == null)
		{
			_logger.LogWarning("Work entry with ID {Id} not found", id);
			return Result.Failure<WorkEntry>(WorkEntryErrors.NotFound(id));
		}

		modify(workEntry, DateTimeHelper.RoundToMinute(Now));

		return await SaveModifiedEntryAsync(uow, workEntry, cancellationToken);
	}

	private async Task<Result<WorkEntry>> SaveModifiedEntryAsync(IUnitOfWork uow, WorkEntry workEntry, CancellationToken cancellationToken)
	{
		if (!workEntry.IsValid())
		{
			_logger.LogWarning("Invalid work entry data after update for entry {Id}, ticket {TicketId} ({StartTime} - {EndTime})", workEntry.Id, workEntry.TicketId, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.InvalidEntryAfterUpdate);
		}

		if (await uow.WorkEntries.HasOverlappingEntriesAsync(workEntry, cancellationToken))
		{
			_logger.LogWarning("Work entry {Id} would overlap with existing entry ({StartTime} - {EndTime})", workEntry.Id, workEntry.StartTime, workEntry.EndTime);
			return Result.Failure<WorkEntry>(WorkEntryErrors.Overlap);
		}

		await uow.WorkEntries.UpdateAsync(workEntry, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);
		_logger.LogInformation("Work entry {Id} updated successfully", workEntry.Id);

		return Result.Success(workEntry);
	}

	public async Task<Result> DeleteWorkEntryAsync(int id, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Deleting work entry {Id}", id);

		await using var uow = await _unitOfWorkFactory.CreateAsync(cancellationToken);

		// Check if entry exists before deleting
		var workEntry = await uow.WorkEntries.GetByIdAsync(id, cancellationToken);
		if (workEntry == null)
		{
			_logger.LogWarning("Work entry with ID {Id} not found", id);
			return Result.Failure(WorkEntryErrors.NotFound(id));
		}

		await uow.WorkEntries.DeleteAsync(id, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);
		_logger.LogInformation("Work entry {Id} deleted successfully", id);

		return Result.Success();
	}

	public async Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		return await _reader.GetByDateRangeAsync(startDate, endDate, cancellationToken);
	}

	public Task<OverlapResolutionPlan> ComputeOverlapResolutionAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		return BuildOverlapPlanAsync(_reader, excludeEntryId, DateTimeHelper.RoundToMinute(startTime), DateTimeHelper.RoundToMinute(endTime), cancellationToken);
	}

	private static async Task<OverlapResolutionPlan> BuildOverlapPlanAsync(IWorkEntryReader reader, int? excludeEntryId, DateTime roundedStart, DateTime? roundedEnd, CancellationToken cancellationToken)
	{
		// For the query: when endTime is null (active/ongoing entry), use +1 minute
		// to detect overlaps at the exact start time (predicate uses StartTime < end)
		// while avoiding matching all future entries
		var queryEnd = roundedEnd ?? roundedStart.AddMinutes(1);

		var overlapping = await reader.GetOverlappingEntriesAsync(excludeEntryId, roundedStart, queryEnd, cancellationToken);

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
	private async Task<bool> IsPlanStillValidAsync(IWorkEntryReader reader, OverlapResolutionPlan plan, int? excludeEntryId, DateTime roundedStart, DateTime? roundedEnd, CancellationToken cancellationToken)
	{
		var currentPlan = await BuildOverlapPlanAsync(reader, excludeEntryId, roundedStart, roundedEnd, cancellationToken);
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

		var applyResult = await ApplyAdjustmentsAsync(uow.WorkEntries, plan.Adjustments, DateTimeHelper.RoundToMinute(now), cancellationToken);
		if (applyResult.IsFailure)
		{
			return Result.Failure<WorkEntry>(applyResult);
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
		var applyResult = await ApplyAdjustmentsAsync(uow.WorkEntries, plan.Adjustments, DateTimeHelper.RoundToMinute(now), cancellationToken);
		if (applyResult.IsFailure)
		{
			return Result.Failure<WorkEntry>(applyResult);
		}

		// Save the updated entry (skip overlap check since we resolved them)
		await uow.WorkEntries.UpdateAsync(workEntry, cancellationToken);
		await uow.SaveChangesAsync(cancellationToken);

		_logger.LogInformation("Work entry {Id} updated successfully with overlap resolution", id);
		return Result.Success(workEntry);
	}

	private async Task<Result> ApplyAdjustmentsAsync(IWorkEntryRepository repository, IReadOnlyList<OverlapAdjustment> adjustments, DateTime roundedNow, CancellationToken cancellationToken)
	{
		foreach (var adjustment in adjustments)
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
