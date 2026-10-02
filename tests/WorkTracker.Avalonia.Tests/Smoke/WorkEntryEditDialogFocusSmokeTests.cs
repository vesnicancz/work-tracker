using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAssertions;
using WorkTracker.Avalonia.Views;

namespace WorkTracker.Avalonia.Tests.Smoke;

/// <summary>
/// CalendarDatePicker hands its own focus to the TextBox inside it. Unless the picker itself is
/// kept out of the tab order, Shift+Tab from that inner TextBox lands on the picker, which hands
/// focus straight back, and backward navigation gets stuck on the date.
/// </summary>
public class WorkEntryEditDialogFocusSmokeTests
{
	[Fact]
	public Task ShiftTab_FromStartDate_MovesToDescription() => UiThread.Dispatch(() =>
	{
		var dialog = new WorkEntryEditDialog();
		dialog.Show();

		try
		{
			var pickers = dialog.GetVisualDescendants().OfType<CalendarDatePicker>().ToList();
			var dateText = pickers[0].GetVisualDescendants().OfType<TextBox>().First();
			var description = dialog.GetVisualDescendants().OfType<TextBox>().First(t => t.AcceptsReturn);

			dateText.Focus(NavigationMethod.Tab);
			dialog.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);

			dialog.FocusManager!.GetFocusedElement().Should().BeSameAs(description);
		}
		finally
		{
			dialog.Close();
		}
	});

	[Fact]
	public Task Tab_FromDescription_MovesIntoStartDate() => UiThread.Dispatch(() =>
	{
		var dialog = new WorkEntryEditDialog();
		dialog.Show();

		try
		{
			var pickers = dialog.GetVisualDescendants().OfType<CalendarDatePicker>().ToList();
			var dateText = pickers[0].GetVisualDescendants().OfType<TextBox>().First();
			var description = dialog.GetVisualDescendants().OfType<TextBox>().First(t => t.AcceptsReturn);

			description.Focus(NavigationMethod.Tab);
			dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);

			dialog.FocusManager!.GetFocusedElement().Should().BeSameAs(dateText);
		}
		finally
		{
			dialog.Close();
		}
	});
}
