using FluentAssertions;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.ViewModels;

public class WorklogPreviewItemTests
{
	private static WorklogPreviewItem CreateItem(DateTime? date = null)
	{
		var d = date ?? new DateTime(2025, 3, 15);
		return new WorklogPreviewItem
		{
			Date = d,
			StartTime = d.AddHours(9),
			EndTime = d.AddHours(10),
			TicketId = "PROJ-1",
			Description = "Test",
			Duration = 3600
		};
	}

	#region TicketIdDisplay

	[Fact]
	public void TicketIdDisplay_WithTicketId_ReturnsTicketId()
	{
		var item = new WorklogPreviewItem { TicketId = "PROJ-123" };

		item.TicketIdDisplay.Should().Be("PROJ-123");
	}

	[Fact]
	public void TicketIdDisplay_WithoutTicketId_ReturnsNoTicketLabel()
	{
		var item = new WorklogPreviewItem { TicketId = null, NoTicketLabel = "(no ticket)" };

		item.TicketIdDisplay.Should().Be("(no ticket)");
	}

	[Fact]
	public void TicketIdDisplay_WithoutTicketIdOrLabel_ReturnsEmpty()
	{
		var item = new WorklogPreviewItem { TicketId = null, NoTicketLabel = null };

		item.TicketIdDisplay.Should().BeEmpty();
	}

	[Fact]
	public void TicketId_Changed_RaisesTicketIdDisplayPropertyChanged()
	{
		var item = new WorklogPreviewItem { TicketId = "OLD-1" };
		var propertyNames = new List<string>();
		item.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName!);

		item.TicketId = "NEW-2";

		propertyNames.Should().Contain(nameof(WorklogPreviewItem.TicketId));
		propertyNames.Should().Contain(nameof(WorklogPreviewItem.TicketIdDisplay));
	}

	#endregion

	#region Duration from times

	[Fact]
	public void StartTime_Changed_UpdatesDuration()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem
		{
			Date = date,
			StartTime = date.AddHours(9),
			EndTime = date.AddHours(11)
		};
		item.Duration.Should().Be(7200); // 2h initial

		item.StartTime = date.AddHours(10);

		item.Duration.Should().Be(3600); // Now 1h
	}

	[Fact]
	public void EndTime_Changed_UpdatesDuration()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem
		{
			Date = date,
			StartTime = date.AddHours(9),
			EndTime = date.AddHours(9).AddMinutes(30)
		};
		item.Duration.Should().Be(1800); // 30m initial

		item.EndTime = date.AddHours(10);

		item.Duration.Should().Be(3600); // Now 1h
	}

	[Fact]
	public void EndTimeBeforeStartTime_DurationIsZero()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem
		{
			Date = date,
			StartTime = date.AddHours(10),
			EndTime = date.AddHours(9)
		};

		item.Duration.Should().Be(0);
	}

	[Fact]
	public void Duration_Changed_UpdatesDurationDisplay()
	{
		var item = new WorklogPreviewItem();
		item.Duration = 5400; // 1h 30m

		item.DurationDisplay.Should().Be("1h 30m");
	}

	[Fact]
	public void Duration_SetToZero_FromNonZero_DisplaysZeroMinutes()
	{
		var item = new WorklogPreviewItem();
		item.Duration = 3600; // Set non-zero first
		item.Duration = 0;   // Then set to zero to trigger cache update

		item.DurationDisplay.Should().Be("0m");
	}

	[Fact]
	public void DurationDisplay_DefaultBeforeAnySet_IsEmpty()
	{
		var item = new WorklogPreviewItem();

		item.DurationDisplay.Should().BeEmpty();
	}

	#endregion

	#region Time display parsing (StartTimeDisplay/EndTimeDisplay setters)

	[Fact]
	public void StartTimeDisplay_ValidTime_UpdatesStartTime()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem { Date = date, StartTime = date.AddHours(8), EndTime = date.AddHours(12) };

		item.StartTimeDisplay = "9:30";

		item.StartTime.Should().Be(date.AddHours(9).AddMinutes(30));
	}

	[Fact]
	public void StartTimeDisplay_TwoDigitFormat_UpdatesStartTime()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem { Date = date, StartTime = date.AddHours(8), EndTime = date.AddHours(12) };

		item.StartTimeDisplay = "09:30";

		item.StartTime.Should().Be(date.AddHours(9).AddMinutes(30));
	}

	[Fact]
	public void StartTimeDisplay_InvalidTime_DoesNotChangeStartTime()
	{
		var date = new DateTime(2025, 3, 15);
		var originalStart = date.AddHours(9);
		var item = new WorklogPreviewItem { Date = date, StartTime = originalStart, EndTime = date.AddHours(10) };

		item.StartTimeDisplay = "invalid";

		item.StartTime.Should().Be(originalStart);
	}

	[Fact]
	public void EndTimeDisplay_ValidTime_UpdatesEndTime()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem { Date = date, StartTime = date.AddHours(9) };

		item.EndTimeDisplay = "17:00";

		item.EndTime.Should().Be(date.AddHours(17));
	}

	[Fact]
	public void EndTimeDisplay_InvalidTime_DoesNotChangeEndTime()
	{
		var date = new DateTime(2025, 3, 15);
		var originalEnd = date.AddHours(17);
		var item = new WorklogPreviewItem { Date = date, StartTime = date.AddHours(9), EndTime = originalEnd };

		item.EndTimeDisplay = "abc";

		item.EndTime.Should().Be(originalEnd);
	}

	[Fact]
	public void StartTimeDisplay_Getter_ReturnsFormattedTime()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem { Date = date };
		item.StartTime = date.AddHours(9).AddMinutes(5);

		item.StartTimeDisplay.Should().Be("09:05");
	}

	[Fact]
	public void EndTimeDisplay_Getter_ReturnsFormattedTime()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem { Date = date, StartTime = date.AddHours(9) };
		item.EndTime = date.AddHours(17).AddMinutes(30);

		item.EndTimeDisplay.Should().Be("17:30");
	}

	#endregion

	#region Save/Restore original values

	[Fact]
	public void SaveAndRestore_RestoresAllValues()
	{
		var item = CreateItem();
		item.SaveOriginalValues();

		// Modify all fields
		item.TicketId = "CHANGED-99";
		item.Description = "Changed";
		item.Duration = 999;
		item.StartTime = item.Date.AddHours(12);
		item.EndTime = item.Date.AddHours(15);

		item.RestoreOriginalValues();

		item.TicketId.Should().Be("PROJ-1");
		item.Description.Should().Be("Test");
		item.Duration.Should().Be(3600);
		item.StartTime.Should().Be(item.Date.AddHours(9));
		item.EndTime.Should().Be(item.Date.AddHours(10));
	}

	[Fact]
	public void RestoreOriginalValues_BeforeSave_RestoresToDefaults()
	{
		var item = new WorklogPreviewItem { TicketId = "PROJ-1" };

		item.RestoreOriginalValues();

		item.TicketId.Should().BeNull(); // Original was never saved, so default (null)
	}

	#endregion

	#region Error state

	[Fact]
	public void HasError_SetTrue_RaisesPropertyChanged()
	{
		var item = new WorklogPreviewItem();
		var changed = false;
		item.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(WorklogPreviewItem.HasError))
			{
				changed = true;
			}
		};

		item.HasError = true;

		changed.Should().BeTrue();
		item.HasError.Should().BeTrue();
	}

	[Fact]
	public void ErrorMessage_Set_RaisesPropertyChanged()
	{
		var item = new WorklogPreviewItem();
		var changed = false;
		item.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(WorklogPreviewItem.ErrorMessage))
			{
				changed = true;
			}
		};

		item.ErrorMessage = "Something went wrong";

		changed.Should().BeTrue();
		item.ErrorMessage.Should().Be("Something went wrong");
	}

	#endregion

	#region PropertyChanged notifications

	[Fact]
	public void StartTime_Changed_RaisesMultiplePropertyNotifications()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem { Date = date, StartTime = date.AddHours(8), EndTime = date.AddHours(12) };
		var propertyNames = new List<string>();
		item.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName!);

		item.StartTime = date.AddHours(10);

		propertyNames.Should().Contain(nameof(WorklogPreviewItem.StartTime));
		propertyNames.Should().Contain(nameof(WorklogPreviewItem.StartTimeDisplay));
		propertyNames.Should().Contain(nameof(WorklogPreviewItem.Duration));
		propertyNames.Should().Contain(nameof(WorklogPreviewItem.DurationDisplay));
	}

	[Fact]
	public void EndTime_Changed_RaisesMultiplePropertyNotifications()
	{
		var date = new DateTime(2025, 3, 15);
		var item = new WorklogPreviewItem { Date = date, StartTime = date.AddHours(9) };
		var propertyNames = new List<string>();
		item.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName!);

		item.EndTime = date.AddHours(15);

		propertyNames.Should().Contain(nameof(WorklogPreviewItem.EndTime));
		propertyNames.Should().Contain(nameof(WorklogPreviewItem.EndTimeDisplay));
		propertyNames.Should().Contain(nameof(WorklogPreviewItem.Duration));
		propertyNames.Should().Contain(nameof(WorklogPreviewItem.DurationDisplay));
	}

	#endregion

	#region DurationDisplay parsing (Aggregated mode)

	[Theory]
	[InlineData("1:30", 90 * 60)]
	[InlineData("01:05", 65 * 60)]
	[InlineData("0:45", 45 * 60)]
	public void DurationDisplay_Set_HhMm_UpdatesDuration(string input, int expectedSeconds)
	{
		var item = new WorklogPreviewItem { IsAggregated = true };

		item.DurationDisplay = input;

		item.Duration.Should().Be(expectedSeconds);
	}

	[Theory]
	[InlineData("2h 30m", 150 * 60)]
	[InlineData("2h", 120 * 60)]
	[InlineData("45m", 45 * 60)]
	[InlineData("1h 0m", 60 * 60)]
	public void DurationDisplay_Set_HoursMinutesForm_UpdatesDuration(string input, int expectedSeconds)
	{
		var item = new WorklogPreviewItem { IsAggregated = true };

		item.DurationDisplay = input;

		item.Duration.Should().Be(expectedSeconds);
	}

	[Theory]
	[InlineData("30", 30 * 60)]
	[InlineData("0", 0)]
	[InlineData("120", 120 * 60)]
	public void DurationDisplay_Set_BareMinutes_UpdatesDuration(string input, int expectedSeconds)
	{
		var item = new WorklogPreviewItem { IsAggregated = true };

		item.DurationDisplay = input;

		item.Duration.Should().Be(expectedSeconds);
	}

	[Theory]
	[InlineData("abc")]
	[InlineData("")]
	[InlineData("  ")]
	[InlineData("-5m")]
	[InlineData("99999999999h")]
	[InlineData("1h -5m")]       // garbage between tokens — anchored regex must reject
	[InlineData("2h foo")]       // trailing junk
	[InlineData("blah 3m")]      // leading junk
	[InlineData("1h 2h")]        // two hours tokens
	[InlineData("5m 10m")]       // two minutes tokens
	[InlineData("2147483647h")]  // int.MaxValue hours — overflow when *60
	public void DurationDisplay_Set_InvalidInput_LeavesDurationUnchanged(string input)
	{
		var item = new WorklogPreviewItem { IsAggregated = true };
		item.DurationDisplay = "1h";
		var durationBefore = item.Duration;

		item.DurationDisplay = input;

		item.Duration.Should().Be(durationBefore);
	}

	[Fact]
	public void DurationDisplay_Set_InvalidInput_RaisesPropertyChangedToRevertBinding()
	{
		var item = new WorklogPreviewItem { IsAggregated = true };
		item.DurationDisplay = "1h"; // sets Duration to 60 * 60 = 3600 s
		var propertyNames = new List<string>();
		item.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName!);

		item.DurationDisplay = "not a duration";

		// TextBox bound to DurationDisplay must snap back to the last valid display value
		propertyNames.Should().Contain(nameof(WorklogPreviewItem.DurationDisplay));
		item.Duration.Should().Be(3600);
	}

	[Fact]
	public void DurationDisplay_Set_NewValueOverridesAggregatedDuration()
	{
		// In aggregated mode the user can edit Duration directly; Start/End must not recalculate it.
		var item = new WorklogPreviewItem
		{
			Date = new DateTime(2025, 3, 15),
			IsAggregated = true,
			StartTime = new DateTime(2025, 3, 15, 9, 0, 0),
			EndTime = new DateTime(2025, 3, 15, 9, 0, 0),
			Duration = 60 * 60
		};

		item.DurationDisplay = "3h 15m";

		item.Duration.Should().Be(195 * 60);
	}

	#endregion
}
