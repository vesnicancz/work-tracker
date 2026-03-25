using FluentAssertions;
using WorkTracker.Application.Services;

namespace WorkTracker.Application.Tests.Services;

public class DateRangeServiceTests
{
	private readonly DateRangeService _service;

	public DateRangeServiceTests()
	{
		_service = new DateRangeService();
	}

	#region GetWeekRange Tests

	[Fact]
	public void GetWeekRange_WithMonday_ShouldReturnCorrectRange()
	{
		// Arrange - Monday, November 4, 2025
		var monday = new DateTime(2025, 11, 3);

		// Act
		var (start, end) = _service.GetWeekRange(monday);

		// Assert
		start.Should().Be(new DateTime(2025, 11, 3)); // Monday
		end.Should().Be(new DateTime(2025, 11, 9)); // Sunday
		start.DayOfWeek.Should().Be(DayOfWeek.Monday);
		end.DayOfWeek.Should().Be(DayOfWeek.Sunday);
	}

	[Fact]
	public void GetWeekRange_WithTuesday_ShouldReturnCorrectRange()
	{
		// Arrange - Tuesday, November 4, 2025
		var tuesday = new DateTime(2025, 11, 4);

		// Act
		var (start, end) = _service.GetWeekRange(tuesday);

		// Assert
		start.Should().Be(new DateTime(2025, 11, 3)); // Monday
		end.Should().Be(new DateTime(2025, 11, 9)); // Sunday
		start.DayOfWeek.Should().Be(DayOfWeek.Monday);
		end.DayOfWeek.Should().Be(DayOfWeek.Sunday);
	}

	[Fact]
	public void GetWeekRange_WithSunday_ShouldReturnCorrectRange()
	{
		// Arrange - Sunday, November 9, 2025
		var sunday = new DateTime(2025, 11, 9);

		// Act
		var (start, end) = _service.GetWeekRange(sunday);

		// Assert
		start.Should().Be(new DateTime(2025, 11, 3)); // Monday
		end.Should().Be(new DateTime(2025, 11, 9)); // Sunday
		start.DayOfWeek.Should().Be(DayOfWeek.Monday);
		end.DayOfWeek.Should().Be(DayOfWeek.Sunday);
	}

	[Fact]
	public void GetWeekRange_WithSaturday_ShouldReturnCorrectRange()
	{
		// Arrange - Saturday, November 8, 2025
		var saturday = new DateTime(2025, 11, 8);

		// Act
		var (start, end) = _service.GetWeekRange(saturday);

		// Assert
		start.Should().Be(new DateTime(2025, 11, 3)); // Monday
		end.Should().Be(new DateTime(2025, 11, 9)); // Sunday
		start.DayOfWeek.Should().Be(DayOfWeek.Monday);
		end.DayOfWeek.Should().Be(DayOfWeek.Sunday);
	}

	[Fact]
	public void GetWeekRange_SpanningMonths_ShouldReturnCorrectRange()
	{
		// Arrange - Monday crossing month boundary
		var lastDayOfOctober = new DateTime(2025, 10, 31); // Friday

		// Act
		var (start, end) = _service.GetWeekRange(lastDayOfOctober);

		// Assert
		start.Should().Be(new DateTime(2025, 10, 27)); // Monday
		end.Should().Be(new DateTime(2025, 11, 2)); // Sunday
		start.Month.Should().Be(10);
		end.Month.Should().Be(11);
	}

	[Fact]
	public void GetWeekRange_ShouldReturn7DaySpan()
	{
		// Arrange
		var anyDate = new DateTime(2025, 11, 5);

		// Act
		var (start, end) = _service.GetWeekRange(anyDate);

		// Assert
		(end - start).Days.Should().Be(6); // 7 days total (6 days difference)
	}

	[Fact]
	public void GetWeekRange_WithTime_ShouldIgnoreTime()
	{
		// Arrange - Date with specific time
		var dateWithTime = new DateTime(2025, 11, 5, 15, 30, 45);

		// Act
		var (start, end) = _service.GetWeekRange(dateWithTime);

		// Assert
		start.TimeOfDay.Should().Be(TimeSpan.Zero);
		end.TimeOfDay.Should().Be(TimeSpan.Zero);
	}

	#endregion GetWeekRange Tests

	#region GetMonthRange Tests

	[Fact]
	public void GetMonthRange_WithFirstDayOfMonth_ShouldReturnCorrectRange()
	{
		// Arrange
		var firstDay = new DateTime(2025, 11, 1);

		// Act
		var (start, end) = _service.GetMonthRange(firstDay);

		// Assert
		start.Should().Be(new DateTime(2025, 11, 1));
		end.Should().Be(new DateTime(2025, 11, 30));
	}

	[Fact]
	public void GetMonthRange_WithLastDayOfMonth_ShouldReturnCorrectRange()
	{
		// Arrange
		var lastDay = new DateTime(2025, 11, 30);

		// Act
		var (start, end) = _service.GetMonthRange(lastDay);

		// Assert
		start.Should().Be(new DateTime(2025, 11, 1));
		end.Should().Be(new DateTime(2025, 11, 30));
	}

	[Fact]
	public void GetMonthRange_WithMiddleOfMonth_ShouldReturnCorrectRange()
	{
		// Arrange
		var middleDay = new DateTime(2025, 11, 15);

		// Act
		var (start, end) = _service.GetMonthRange(middleDay);

		// Assert
		start.Should().Be(new DateTime(2025, 11, 1));
		end.Should().Be(new DateTime(2025, 11, 30));
	}

	[Fact]
	public void GetMonthRange_February_ShouldReturnCorrectRange()
	{
		// Arrange - Non-leap year
		var februaryDate = new DateTime(2025, 2, 15);

		// Act
		var (start, end) = _service.GetMonthRange(februaryDate);

		// Assert
		start.Should().Be(new DateTime(2025, 2, 1));
		end.Should().Be(new DateTime(2025, 2, 28));
	}

	[Fact]
	public void GetMonthRange_FebruaryLeapYear_ShouldReturnCorrectRange()
	{
		// Arrange - Leap year
		var februaryDate = new DateTime(2024, 2, 15);

		// Act
		var (start, end) = _service.GetMonthRange(februaryDate);

		// Assert
		start.Should().Be(new DateTime(2024, 2, 1));
		end.Should().Be(new DateTime(2024, 2, 29));
	}

	[Fact]
	public void GetMonthRange_December_ShouldReturnCorrectRange()
	{
		// Arrange
		var decemberDate = new DateTime(2025, 12, 15);

		// Act
		var (start, end) = _service.GetMonthRange(decemberDate);

		// Assert
		start.Should().Be(new DateTime(2025, 12, 1));
		end.Should().Be(new DateTime(2025, 12, 31));
	}

	[Fact]
	public void GetMonthRange_WithTime_ShouldIgnoreTime()
	{
		// Arrange
		var dateWithTime = new DateTime(2025, 11, 15, 14, 30, 45);

		// Act
		var (start, end) = _service.GetMonthRange(dateWithTime);

		// Assert
		start.TimeOfDay.Should().Be(TimeSpan.Zero);
		end.TimeOfDay.Should().Be(TimeSpan.Zero);
	}

	#endregion GetMonthRange Tests

	#region GetDatesInRange Tests

	[Fact]
	public void GetDatesInRange_WithSingleDay_ShouldReturnOneDate()
	{
		// Arrange
		var date = new DateTime(2025, 11, 5);

		// Act
		var dates = _service.GetDatesInRange(date, date).ToList();

		// Assert
		dates.Should().HaveCount(1);
		dates[0].Should().Be(date.Date);
	}

	[Fact]
	public void GetDatesInRange_WithOneWeek_ShouldReturn7Dates()
	{
		// Arrange
		var start = new DateTime(2025, 11, 3); // Monday
		var end = new DateTime(2025, 11, 9); // Sunday

		// Act
		var dates = _service.GetDatesInRange(start, end).ToList();

		// Assert
		dates.Should().HaveCount(7);
		dates.First().Should().Be(start);
		dates.Last().Should().Be(end);
	}

	[Fact]
	public void GetDatesInRange_ShouldReturnConsecutiveDates()
	{
		// Arrange
		var start = new DateTime(2025, 11, 5);
		var end = new DateTime(2025, 11, 8);

		// Act
		var dates = _service.GetDatesInRange(start, end).ToList();

		// Assert
		dates.Should().HaveCount(4);
		dates[0].Should().Be(new DateTime(2025, 11, 5));
		dates[1].Should().Be(new DateTime(2025, 11, 6));
		dates[2].Should().Be(new DateTime(2025, 11, 7));
		dates[3].Should().Be(new DateTime(2025, 11, 8));
	}

	[Fact]
	public void GetDatesInRange_SpanningMonths_ShouldIncludeAllDates()
	{
		// Arrange
		var start = new DateTime(2025, 10, 30);
		var end = new DateTime(2025, 11, 2);

		// Act
		var dates = _service.GetDatesInRange(start, end).ToList();

		// Assert
		dates.Should().HaveCount(4);
		dates[0].Should().Be(new DateTime(2025, 10, 30));
		dates[1].Should().Be(new DateTime(2025, 10, 31));
		dates[2].Should().Be(new DateTime(2025, 11, 1));
		dates[3].Should().Be(new DateTime(2025, 11, 2));
	}

	[Fact]
	public void GetDatesInRange_WithTime_ShouldReturnDatesWithoutTime()
	{
		// Arrange
		var start = new DateTime(2025, 11, 5, 10, 30, 0);
		var end = new DateTime(2025, 11, 7, 18, 45, 30);

		// Act
		var dates = _service.GetDatesInRange(start, end).ToList();

		// Assert
		dates.Should().HaveCount(3);
		dates.All(d => d.TimeOfDay == TimeSpan.Zero).Should().BeTrue();
	}

	[Fact]
	public void GetDatesInRange_ShouldBeLazyEvaluated()
	{
		// Arrange
		var start = new DateTime(2025, 11, 1);
		var end = new DateTime(2025, 11, 30);

		// Act
		var datesEnumerable = _service.GetDatesInRange(start, end);

		// Assert - Should not throw, enumerable is lazy
		datesEnumerable.Should().NotBeNull();

		// When materialized
		var dates = datesEnumerable.ToList();
		dates.Should().HaveCount(30);
	}

	[Fact]
	public void GetDatesInRange_FullYear_ShouldReturn365Dates()
	{
		// Arrange - Non-leap year
		var start = new DateTime(2025, 1, 1);
		var end = new DateTime(2025, 12, 31);

		// Act
		var dates = _service.GetDatesInRange(start, end).ToList();

		// Assert
		dates.Should().HaveCount(365);
	}

	#endregion GetDatesInRange Tests
}