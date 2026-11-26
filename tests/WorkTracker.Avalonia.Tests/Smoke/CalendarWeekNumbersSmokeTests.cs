using CalendarWeekRule = System.Globalization.CalendarWeekRule;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using FluentAssertions;

namespace WorkTracker.Avalonia.Tests.Smoke;

/// <summary>
/// Week numbers come from the app-wide CalendarDatePicker style, so every picker shows them, and
/// they are drawn by the theme's CalendarItem template, which a style override could hide.
/// </summary>
public class CalendarWeekNumbersSmokeTests
{
	[Fact]
	public Task CalendarDatePicker_ShowsIsoWeekNumbersInItsCalendar() => UiThread.Dispatch(() =>
	{
		var picker = new CalendarDatePicker
		{
			// Fixed rather than taken from the test machine's culture.
			FirstDayOfWeek = DayOfWeek.Monday,
			WeekNumberRule = CalendarWeekRule.FirstFourDayWeek,
			DisplayDate = new DateTime(2026, 1, 15),
		};
		var window = new Window { Content = picker };
		window.Show();

		try
		{
			picker.IsWeekNumberVisible.Should().BeTrue("the app style turns week numbers on for every picker");

			picker.IsDropDownOpen = true;
			window.UpdateLayout();

			// The calendar lives in the template's popup, outside the picker's own trees.
			var popup = picker.GetVisualDescendants().OfType<Popup>().Single();
			var calendar = popup.Child.Should().BeOfType<Calendar>().Subject;
			calendar.UpdateLayout();
			var labels = calendar.GetVisualDescendants().OfType<Grid>()
				.Single(g => g.Name == "PART_ElementWeekNumberLabels");

			labels.IsVisible.Should().BeTrue();
			// January 2026 starts on a Thursday, so the first row (from Monday 29 December) is ISO week 1.
			labels.Children.OfType<ContentControl>().Skip(1).Select(c => c.Content?.ToString())
				.Should().StartWith(["1", "2", "3"]);
		}
		finally
		{
			picker.IsDropDownOpen = false;
			window.Close();
		}
	});
}
