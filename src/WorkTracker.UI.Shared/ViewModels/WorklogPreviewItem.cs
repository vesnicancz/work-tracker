using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkTracker.UI.Shared.Helpers;

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

	// Selection state
	private bool _isSelected = true;

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

	/// <summary>
	/// Whether this row represents an aggregated submission entry (grouped by code+description
	/// per day). In aggregated mode <see cref="StartTime"/> is representative only,
	/// <see cref="EndTime"/> is not meaningful, and <see cref="Duration"/> is directly editable.
	/// </summary>
	public bool IsAggregated { get; set; }

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}

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
		set
		{
			if (SetProperty(ref _ticketId, value))
			{
				OnPropertyChanged(nameof(TicketIdDisplay));
			}
		}
	}

	/// <summary>
	/// Display-only ticket ID that shows a placeholder for entries without a ticket.
	/// Set by the orchestrator during preview loading.
	/// </summary>
	public string? NoTicketLabel { get; set; }

	public string TicketIdDisplay => TicketId ?? NoTicketLabel ?? string.Empty;

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

	private static readonly string[] TimeFormats = ["h\\:mm", "hh\\:mm", "h\\:m", "hh\\:m"];

	public string StartTimeDisplay
	{
		get => _startTimeDisplay;
		set
		{
			if (TimeSpan.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, out var time))
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
			if (TimeSpan.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, out var time))
			{
				EndTime = Date.Date.Add(time);
			}
		}
	}

	public string DurationDisplay
	{
		get => _durationDisplay;
		set
		{
			if (TryParseDuration(value, out var seconds))
			{
				Duration = seconds;
			}
			else
			{
				// Notify so the bound TextBox snaps back to the last valid display value
				OnPropertyChanged(nameof(DurationDisplay));
			}
		}
	}

	// Anchored whole-string match: optional "Xh", optional whitespace, optional "Ym".
	// Must consume the entire input — rejects garbage like "1h -5m", "2h foo", "blah 3m".
	private static readonly System.Text.RegularExpressions.Regex HoursMinutesRegex = new(
		@"^(?:(\d+)\s*h)?\s*(?:(\d+)\s*m)?$",
		System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

	private static bool TryParseDuration(string? text, out int seconds)
	{
		seconds = 0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		var trimmed = text.Trim();

		// Reject explicit negative input — downstream parsers only capture digit groups.
		if (trimmed.StartsWith('-'))
		{
			return false;
		}

		// H:MM or HH:MM form
		if (TimeSpan.TryParseExact(trimmed, TimeFormats, CultureInfo.InvariantCulture, out var ts) && ts >= TimeSpan.Zero)
		{
			seconds = (int)ts.TotalSeconds;
			return true;
		}

		// "Xh Ym", "Xh", "Ym" form (matches DurationFormatter output)
		var match = HoursMinutesRegex.Match(trimmed);
		if (match.Success && (match.Groups[1].Success || match.Groups[2].Success))
		{
			long totalMinutes = 0;
			if (match.Groups[1].Success)
			{
				if (!long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
					hours > int.MaxValue / 60)
				{
					return false;
				}
				totalMinutes += hours * 60;
			}
			if (match.Groups[2].Success)
			{
				if (!long.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
					minutes > int.MaxValue)
				{
					return false;
				}
				totalMinutes += minutes;
			}

			return TryConvertMinutesToSeconds(totalMinutes, out seconds);
		}

		// Bare integer = minutes
		if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutesOnly) && minutesOnly >= 0)
		{
			return TryConvertMinutesToSeconds(minutesOnly, out seconds);
		}

		return false;
	}

	private static bool TryConvertMinutesToSeconds(long totalMinutes, out int seconds)
	{
		seconds = 0;
		if (totalMinutes < 0 || totalMinutes > int.MaxValue / 60)
		{
			return false;
		}
		seconds = (int)(totalMinutes * 60);
		return true;
	}

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
		IsSelected = true;
	}

	private void UpdateDurationFromTimes()
	{
		// In aggregated mode Start/End are not a real interval — Duration is driven by the user directly.
		if (IsAggregated)
		{
			return;
		}

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