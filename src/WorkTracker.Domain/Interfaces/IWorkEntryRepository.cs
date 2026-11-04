using WorkTracker.Domain.Entities;

namespace WorkTracker.Domain.Interfaces;

/// <summary>
/// Work entries inside a unit of work. Changes reach the database when the unit of work saves them.
/// </summary>
public interface IWorkEntryRepository : IWorkEntryReader
{
	Task<WorkEntry> AddAsync(WorkEntry workEntry, CancellationToken cancellationToken);

	Task UpdateAsync(WorkEntry workEntry, CancellationToken cancellationToken);

	Task DeleteAsync(int id, CancellationToken cancellationToken);

	Task<bool> HasOverlappingEntriesAsync(WorkEntry workEntry, CancellationToken cancellationToken);

	/// <summary>
	/// Lightweight existence check for overlapping entries. Uses EF's <c>AnyAsync</c> — produces
	/// a <c>SELECT EXISTS</c>-style query without materializing matching rows. Prefer this over
	/// <see cref="IWorkEntryReader.GetOverlappingEntriesAsync"/> when only existence is needed.
	/// </summary>
	Task<bool> HasOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken);
}
