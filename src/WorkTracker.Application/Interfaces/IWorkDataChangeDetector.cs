namespace WorkTracker.Application.Interfaces;

/// <summary>
/// Tells when the stored work entries last changed, whichever process changed them. The CLI and the
/// GUI share one database, and neither is told about the other's writes.
/// </summary>
public interface IWorkDataChangeDetector
{
	/// <summary>
	/// Gets the time of the last change, or null when it cannot be told right now (e.g. the database
	/// is on a removable drive that is not connected).
	/// </summary>
	DateTime? GetLastChangeUtc();
}
