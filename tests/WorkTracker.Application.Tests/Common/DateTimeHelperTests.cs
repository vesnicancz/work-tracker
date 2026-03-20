using FluentAssertions;
using WorkTracker.Application.Common;

namespace WorkTracker.Application.Tests.Common;

public class DateTimeHelperTests
{
	#region RoundToMinute (DateTime) Tests

	[Fact]
	public void RoundToMinute_WithExactMinute_ShouldReturnSameValue()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 0, 0);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(dateTime);
	}

	[Fact]
	public void RoundToMinute_WithSeconds_ShouldRemoveSeconds()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 45, 0);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(new DateTime(2025, 11, 2, 14, 30, 0, 0));
		result.Second.Should().Be(0);
	}

	[Fact]
	public void RoundToMinute_WithMilliseconds_ShouldRemoveMilliseconds()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 0, 500);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(new DateTime(2025, 11, 2, 14, 30, 0, 0));
		result.Millisecond.Should().Be(0);
	}

	[Fact]
	public void RoundToMinute_WithSecondsAndMilliseconds_ShouldRemoveBoth()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 45, 750);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(new DateTime(2025, 11, 2, 14, 30, 0, 0));
		result.Second.Should().Be(0);
		result.Millisecond.Should().Be(0);
	}

	[Fact]
	public void RoundToMinute_ShouldPreserveDateComponents()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 45, 500);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Year.Should().Be(2025);
		result.Month.Should().Be(11);
		result.Day.Should().Be(2);
		result.Hour.Should().Be(14);
		result.Minute.Should().Be(30);
	}

	[Fact]
	public void RoundToMinute_WithUtcKind_ShouldPreserveKind()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 45, DateTimeKind.Utc);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Kind.Should().Be(DateTimeKind.Utc);
	}

	[Fact]
	public void RoundToMinute_WithLocalKind_ShouldPreserveKind()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 45, DateTimeKind.Local);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Kind.Should().Be(DateTimeKind.Local);
	}

	[Fact]
	public void RoundToMinute_WithUnspecifiedKind_ShouldPreserveKind()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 45, DateTimeKind.Unspecified);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Kind.Should().Be(DateTimeKind.Unspecified);
	}

	[Fact]
	public void RoundToMinute_WithMidnight_ShouldHandleCorrectly()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 0, 0, 30);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(new DateTime(2025, 11, 2, 0, 0, 0));
	}

	[Fact]
	public void RoundToMinute_WithEndOfDay_ShouldHandleCorrectly()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 23, 59, 59, 999);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(new DateTime(2025, 11, 2, 23, 59, 0, 0));
	}

	#endregion RoundToMinute (DateTime) Tests

	#region RoundToMinute (DateTime?) Tests

	[Fact]
	public void RoundToMinute_WithNullableValue_ShouldRoundCorrectly()
	{
		// Arrange
		DateTime? dateTime = new DateTime(2025, 11, 2, 14, 30, 45);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().NotBeNull();
		result.Should().Be(new DateTime(2025, 11, 2, 14, 30, 0));
	}

	[Fact]
	public void RoundToMinute_WithNullValue_ShouldReturnNull()
	{
		// Arrange
		DateTime? dateTime = null;

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().BeNull();
	}

	[Fact]
	public void RoundToMinute_WithNullableExactMinute_ShouldReturnSameValue()
	{
		// Arrange
		DateTime? dateTime = new DateTime(2025, 11, 2, 14, 30, 0);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(dateTime);
	}

	[Fact]
	public void RoundToMinute_WithNullableUtcKind_ShouldPreserveKind()
	{
		// Arrange
		DateTime? dateTime = new DateTime(2025, 11, 2, 14, 30, 45, DateTimeKind.Utc);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().NotBeNull();
		result!.Value.Kind.Should().Be(DateTimeKind.Utc);
	}

	#endregion RoundToMinute (DateTime?) Tests

	#region Edge Cases and Special Scenarios

	[Fact]
	public void RoundToMinute_WithMinValue_ShouldNotThrow()
	{
		// Arrange
		var dateTime = DateTime.MinValue;

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(DateTime.MinValue);
	}

	[Fact]
	public void RoundToMinute_WithMaxValue_ShouldNotThrow()
	{
		// Arrange - Use a valid DateTime that won't overflow
		var dateTime = new DateTime(9999, 12, 31, 23, 59, 59, 999);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(new DateTime(9999, 12, 31, 23, 59, 0, 0));
	}

	[Fact]
	public void RoundToMinute_MultipleCallsSameInput_ShouldBeIdempotent()
	{
		// Arrange
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 45, 500);

		// Act
		var result1 = DateTimeHelper.RoundToMinute(dateTime);
		var result2 = DateTimeHelper.RoundToMinute(result1);

		// Assert
		result1.Should().Be(result2);
	}

	[Fact]
	public void RoundToMinute_WithLeapSecond_ShouldHandleCorrectly()
	{
		// Arrange - Simulating a time with 59 seconds
		var dateTime = new DateTime(2025, 11, 2, 14, 30, 59, 999);

		// Act
		var result = DateTimeHelper.RoundToMinute(dateTime);

		// Assert
		result.Should().Be(new DateTime(2025, 11, 2, 14, 30, 0, 0));
	}

	#endregion Edge Cases and Special Scenarios
}