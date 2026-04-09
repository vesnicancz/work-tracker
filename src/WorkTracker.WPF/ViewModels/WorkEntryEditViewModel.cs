using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.WPF.ViewModels;

/// <summary>
/// ViewModel for editing work entries
/// </summary>
public class WorkEntryEditViewModel : ViewModelBase
{
	private readonly IWorkEntryEditOrchestrator _orchestrator;
	private readonly TimeProvider _timeProvider;
	private readonly ILocalizationService _localization;
	private readonly ILogger<WorkEntryEditViewModel> _logger;
	private bool _isNewEntry = true;
	private int _entryId;
	private string? _ticketId;
	private string? _description;
	private DateTime _startDate;
	private TimeSpan _startTime;
	private DateTime? _endDate;
	private TimeSpan? _endTime;
	private bool _hasEndTime;
	private string? _validationError;

	public WorkEntryEditViewModel(
		IWorkEntryEditOrchestrator orchestrator,
		ILocalizationService localization,
		TimeProvider timeProvider,
		ILogger<WorkEntryEditViewModel> logger)
	{
		_orchestrator = orchestrator;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;

		SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
		CancelCommand = new RelayCommand(Cancel);
	}

	#region Properties

	public bool IsNewEntry => _isNewEntry;

	public string DialogTitle => IsNewEntry ? _localization["NewWorkEntry"] : _localization["EditWorkEntry"];

	public int EntryId
	{
		get => _entryId;
		set => SetProperty(ref _entryId, value);
	}

	public string? TicketId
	{
		get => _ticketId;
		set
		{
			if (SetProperty(ref _ticketId, value))
			{
				ValidateInput();
			}
		}
	}

	public string? Description
	{
		get => _description;
		set
		{
			if (SetProperty(ref _description, value))
			{
				ValidateInput();
			}
		}
	}

	public DateTime StartDate
	{
		get => _startDate;
		set
		{
			if (SetProperty(ref _startDate, value))
			{
				ValidateInput();
			}
		}
	}

	public TimeSpan StartTime
	{
		get => _startTime;
		set
		{
			if (SetProperty(ref _startTime, value))
			{
				ValidateInput();
			}
		}
	}

	public DateTime? EndDate
	{
		get => _endDate;
		set
		{
			if (SetProperty(ref _endDate, value))
			{
				ValidateInput();
			}
		}
	}

	public TimeSpan? EndTime
	{
		get => _endTime;
		set
		{
			if (SetProperty(ref _endTime, value))
			{
				ValidateInput();
			}
		}
	}

	public bool HasEndTime
	{
		get => _hasEndTime;
		set
		{
			if (SetProperty(ref _hasEndTime, value))
			{
				if (!value)
				{
					EndDate = null;
					EndTime = null;
				}
				else if (EndDate == null)
				{
					var now = DateTimeHelper.RoundToMinute(_timeProvider.GetLocalNow().DateTime);
					EndDate = StartDate.Date;
					EndTime = new TimeSpan(now.Hour, now.Minute, 0);
				}
				ValidateInput();
			}
		}
	}

	public string? ValidationError
	{
		get => _validationError;
		set => SetProperty(ref _validationError, value);
	}

	public DateTime StartDateTime => StartDate.Date + StartTime;

	public DateTime? EndDateTime
	{
		get
		{
			if (EndDate.HasValue && EndTime.HasValue)
			{
				return EndDate.Value.Date + EndTime.Value;
			}
			return null;
		}
	}

	public Action? CloseAction { get; set; }
	public bool DialogResult { get; set; }

	#endregion Properties

	#region Commands

	public ICommand SaveCommand { get; }
	public ICommand CancelCommand { get; }

	#endregion Commands

	#region Initialization

	public void InitializeForEdit(WorkEntry workEntry)
	{
		_isNewEntry = false;
		EntryId = workEntry.Id;
		TicketId = workEntry.TicketId;
		Description = workEntry.Description;
		StartDate = workEntry.StartTime.Date;
		StartTime = workEntry.StartTime.TimeOfDay;

		if (workEntry.EndTime.HasValue)
		{
			HasEndTime = true;
			EndDate = workEntry.EndTime.Value.Date;
			EndTime = workEntry.EndTime.Value.TimeOfDay;
		}
		else
		{
			HasEndTime = false;
		}

		ValidateInput();
	}

	public void InitializeForNew(string? ticketId = null, string? description = null, DateTime? date = null, DateTime? startTime = null, DateTime? endTime = null)
	{
		_isNewEntry = true;
		var now = DateTimeHelper.RoundToMinute(_timeProvider.GetLocalNow().DateTime);
		StartDate = date ?? startTime?.Date ?? now.Date;
		StartTime = startTime?.TimeOfDay ?? new TimeSpan(now.Hour, now.Minute, 0);
		TicketId = ticketId;
		Description = description;

		if (endTime.HasValue)
		{
			HasEndTime = true;
			EndDate = endTime.Value.Date;
			EndTime = endTime.Value.TimeOfDay;
		}
		else
		{
			HasEndTime = false;
		}

		ValidateInput();
	}

	#endregion Initialization

	private void ValidateInput()
	{
		ValidationError = _orchestrator.Validate(TicketId, Description, HasEndTime, StartDateTime, EndDateTime);
		((IAsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
	}

	private bool CanSave() => string.IsNullOrEmpty(ValidationError);

	private async Task SaveAsync()
	{
		try
		{
			var result = _isNewEntry
				? await _orchestrator.SaveNewAsync(TicketId, StartDateTime, EndDateTime, Description, CancellationToken.None)
				: await _orchestrator.SaveExistingAsync(EntryId, TicketId, StartDateTime, EndDateTime, Description, CancellationToken.None);

			if (result.IsFailure)
			{
				ValidationError = result.Error;
				return;
			}

			if (!result.Value)
			{
				return;
			}

			DialogResult = true;
			CloseAction?.Invoke();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error saving work entry");
			ValidationError = _localization.GetFormattedString("FailedToSave", ex.Message);
		}
	}

	private void Cancel()
	{
		DialogResult = false;
		CloseAction?.Invoke();
	}
}