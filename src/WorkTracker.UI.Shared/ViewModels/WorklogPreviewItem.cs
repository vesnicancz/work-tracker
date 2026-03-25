using CommunityToolkit.Mvvm.ComponentModel;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// Preview item for worklog submission
/// </summary>
public class WorklogPreviewItem : ObservableObject
{
	private string? _ticketId;
	private string? _description;
	private int _duration;
	private DateTime _startTime;
	private DateTime _endTime;

	// Cached display strings for performance
	private string _startTimeDisplay = string.Empty;

	private string _endTimeDisplay = string.Empty;
	private string _durationDisplay = string.Empty;

	// Error state
	private bool _hasError;

	private string? _errorMessage;

	// Original values for reset
	private string? _originalTicketId;

	private string? _originalDescription;
	private int _originalDuration;
	private DateTime _originalStartTime;
	private DateTime _originalEndTime;

	public DateTime Date { get; set; }

	public bool IsDateHeader { get; set; }

	public string? DateDisplay { get; set; }

	public bool HasError
	{
		get => _hasError;
		set => SetProperty(ref _hasError, value);
	}

	public string? ErrorMessage
	{
		get => _errorMessage;
		set => SetProperty(ref _errorMessage, value);
	}

	public string? TicketId
	{
		get => _ticketId;
		set => SetProperty(ref _ticketId, value);
	}

	public string? Description
	{
		get => _description;
		set => SetProperty(ref _description, value);
	}

	public int Duration
	{
		get => _duration;
		set
		{
			if (SetProperty(ref _duration, value))
			{
				UpdateDurationDisplayCache();
			}
		}
	}

	public DateTime StartTime
	{
		get => _startTime;
		set
		{
			if (SetProperty(ref _startTime, value))
			{
				UpdateStartTimeDisplayCache();
				UpdateDurationFromTimes();
			}
		}
	}

	public DateTime EndTime
	{
		get => _endTime;
		set
		{
			if (SetProperty(ref _endTime, value))
			{
				UpdateEndTimeDisplayCache();
				UpdateDurationFromTimes();
			}
		}
	}

	public string StartTimeDisplay
	{
		get => _startTimeDisplay;
		set
		{
			if (TimeSpan.TryParse(value, out var time))
			{
				StartTime = Date.Date.Add(time);
			}
		}
	}

	public string EndTimeDisplay
	{
		get => _endTimeDisplay;
		set
		{
			if (TimeSpan.TryParse(value, out var time))
			{
				EndTime = Date.Date.Add(time);
			}
		}
	}

	public string DurationDisplay => _durationDisplay;

	private void UpdateStartTimeDisplayCache()
	{
		_startTimeDisplay = StartTime.ToString("HH:mm");
		OnPropertyChanged(nameof(StartTimeDisplay));
	}

	private void UpdateEndTimeDisplayCache()
	{
		_endTimeDisplay = EndTime.ToString("HH:mm");
		OnPropertyChanged(nameof(EndTimeDisplay));
	}

	private void UpdateDurationDisplayCache()
	{
		_durationDisplay = DurationFormatter.Format(Duration);
		OnPropertyChanged(nameof(DurationDisplay));
	}

	public void SaveOriginalValues()
	{
		_originalTicketId = TicketId;
		_originalDescription = Description;
		_originalDuration = Duration;
		_originalStartTime = StartTime;
		_originalEndTime = EndTime;
	}

	public void RestoreOriginalValues()
	{
		TicketId = _originalTicketId;
		Description = _originalDescription;
		Duration = _originalDuration;
		StartTime = _originalStartTime;
		EndTime = _originalEndTime;
	}

	private void UpdateDurationFromTimes()
	{
		if (EndTime > StartTime)
		{
			Duration = (int)(EndTime - StartTime).TotalSeconds;
		}
		else
		{
			Duration = 0;
		}
	}
}