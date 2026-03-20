using WorkTracker.Domain.DTOs;

namespace WorkTracker.Application.DTOs;

public class WorklogSubmissionDto
{
	public List<WorklogDto> Worklogs { get; set; } = new();

	public DateTime SubmissionDate { get; set; }
}