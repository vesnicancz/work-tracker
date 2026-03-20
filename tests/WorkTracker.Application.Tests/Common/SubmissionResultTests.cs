using FluentAssertions;
using WorkTracker.Application.Common;

namespace WorkTracker.Application.Tests.Common;

public class SubmissionResultTests
{
	[Fact]
	public void IsSuccess_WithAllSuccessful_ShouldReturnTrue()
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = 5,
			SuccessfulEntries = 5,
			FailedEntries = 0
		};

		// Act & Assert
		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public void IsSuccess_WithSomeFailures_ShouldReturnFalse()
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = 5,
			SuccessfulEntries = 3,
			FailedEntries = 2
		};

		// Act & Assert
		result.IsSuccess.Should().BeFalse();
	}

	[Fact]
	public void IsSuccess_WithZeroEntries_ShouldReturnFalse()
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = 0,
			SuccessfulEntries = 0,
			FailedEntries = 0
		};

		// Act & Assert
		result.IsSuccess.Should().BeFalse();
	}

	[Fact]
	public void IsSuccess_WithAllFailed_ShouldReturnFalse()
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = 5,
			SuccessfulEntries = 0,
			FailedEntries = 5
		};

		// Act & Assert
		result.IsSuccess.Should().BeFalse();
	}

	[Fact]
	public void HasPartialSuccess_WithSomeSuccessAndSomeFailures_ShouldReturnTrue()
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = 5,
			SuccessfulEntries = 3,
			FailedEntries = 2
		};

		// Act & Assert
		result.HasPartialSuccess.Should().BeTrue();
	}

	[Fact]
	public void HasPartialSuccess_WithAllSuccessful_ShouldReturnFalse()
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = 5,
			SuccessfulEntries = 5,
			FailedEntries = 0
		};

		// Act & Assert
		result.HasPartialSuccess.Should().BeFalse();
	}

	[Fact]
	public void HasPartialSuccess_WithAllFailed_ShouldReturnFalse()
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = 5,
			SuccessfulEntries = 0,
			FailedEntries = 5
		};

		// Act & Assert
		result.HasPartialSuccess.Should().BeFalse();
	}

	[Fact]
	public void HasPartialSuccess_WithNoSuccesses_ShouldReturnFalse()
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = 3,
			SuccessfulEntries = 0,
			FailedEntries = 3
		};

		// Act & Assert
		result.HasPartialSuccess.Should().BeFalse();
	}

	[Fact]
	public void Errors_ShouldBeInitializedAsEmptyList()
	{
		// Arrange & Act
		var result = new SubmissionResult();

		// Assert
		result.Errors.Should().NotBeNull();
		result.Errors.Should().BeEmpty();
	}

	[Fact]
	public void EntriesByDate_ShouldBeInitializedAsEmptyDictionary()
	{
		// Arrange & Act
		var result = new SubmissionResult();

		// Assert
		result.EntriesByDate.Should().NotBeNull();
		result.EntriesByDate.Should().BeEmpty();
	}

	[Fact]
	public void Errors_ShouldAllowAddingErrors()
	{
		// Arrange
		var result = new SubmissionResult();
		var error = new SubmissionError
		{
			ErrorMessage = "Test error"
		};

		// Act
		result.Errors.Add(error);

		// Assert
		result.Errors.Should().HaveCount(1);
		result.Errors.First().ErrorMessage.Should().Be("Test error");
	}

	[Fact]
	public void EntriesByDate_ShouldAllowAddingEntries()
	{
		// Arrange
		var result = new SubmissionResult();
		var date = DateTime.Today;

		// Act
		result.EntriesByDate[date] = 5;

		// Assert
		result.EntriesByDate.Should().HaveCount(1);
		result.EntriesByDate[date].Should().Be(5);
	}

	[Theory]
	[InlineData(10, 10, 0, true, false)]   // All successful
	[InlineData(10, 5, 5, false, true)]    // Partial success
	[InlineData(10, 0, 10, false, false)]  // All failed
	[InlineData(0, 0, 0, false, false)]    // No entries
	[InlineData(10, 1, 9, false, true)]    // Mostly failed but partial success
	public void CombinedScenarios_ShouldCalculateCorrectly(
		int total,
		int successful,
		int failed,
		bool expectedIsSuccess,
		bool expectedHasPartialSuccess)
	{
		// Arrange
		var result = new SubmissionResult
		{
			TotalEntries = total,
			SuccessfulEntries = successful,
			FailedEntries = failed
		};

		// Act & Assert
		result.IsSuccess.Should().Be(expectedIsSuccess);
		result.HasPartialSuccess.Should().Be(expectedHasPartialSuccess);
	}
}