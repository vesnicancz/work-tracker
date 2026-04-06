using WorkTracker.Domain.Entities;

namespace WorkTracker.Tests.Common.Builders;

/// <summary>
/// Fluent builder for creating <see cref="WorkEntry"/> instances in tests.
/// Uses <see cref="WorkEntry.Reconstitute"/> internally — requires InternalsVisibleTo access.
/// </summary>
public sealed class WorkEntryBuilder
{
	private int _id = 1;
	private string? _ticketId = "PROJ-1";
	private DateTime _startTime = new(2026, 1, 15, 9, 0, 0);
	private DateTime? _endTime = new DateTime(2026, 1, 15, 10, 0, 0);
	private string? _description;
	private bool _isActive;
	private DateTime _createdAt = new(2026, 1, 15, 9, 0, 0);
	private DateTime? _updatedAt;

	public WorkEntryBuilder WithId(int id) { _id = id; return this; }
	public WorkEntryBuilder WithTicketId(string? ticketId) { _ticketId = ticketId; return this; }
	public WorkEntryBuilder WithDescription(string? description) { _description = description; return this; }
	public WorkEntryBuilder WithStartTime(DateTime startTime) { _startTime = startTime; return this; }
	public WorkEntryBuilder WithEndTime(DateTime? endTime) { _endTime = endTime; return this; }
	public WorkEntryBuilder WithUpdatedAt(DateTime? updatedAt) { _updatedAt = updatedAt; return this; }

	public WorkEntryBuilder Active()
	{
		_endTime = null;
		_isActive = true;
		return this;
	}

	public WorkEntryBuilder Completed(DateTime endTime)
	{
		_endTime = endTime;
		_isActive = false;
		return this;
	}

	public WorkEntryBuilder WithTimes(int startHour, int endHour)
	{
		_startTime = _startTime.Date.AddHours(startHour);
		_endTime = _startTime.Date.AddHours(endHour);
		_isActive = false;
		return this;
	}

	public WorkEntryBuilder WithTimes(DateTime start, DateTime end)
	{
		_startTime = start;
		_endTime = end;
		_isActive = false;
		return this;
	}

	public WorkEntry Build() => WorkEntry.Reconstitute(
		_id, _ticketId, _startTime, _endTime, _description, _isActive, _createdAt, _updatedAt);
}
