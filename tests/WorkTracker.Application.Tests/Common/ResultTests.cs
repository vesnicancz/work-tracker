using FluentAssertions;
using WorkTracker.Application.Common;

namespace WorkTracker.Application.Tests.Common;

public class ResultTests
{
	[Fact]
	public void Success_ShouldCreateSuccessfulResult()
	{
		// Act
		var result = Result.Success();

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.IsFailure.Should().BeFalse();
		result.Error.Should().BeEmpty();
	}

	[Fact]
	public void Failure_WithError_ShouldCreateFailureResult()
	{
		// Arrange
		var errorMessage = "Something went wrong";

		// Act
		var result = Result.Failure(errorMessage);

		// Assert
		result.IsSuccess.Should().BeFalse();
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(errorMessage);
	}

	[Fact]
	public void GenericSuccess_WithValue_ShouldCreateSuccessfulResult()
	{
		// Arrange
		var value = "test value";

		// Act
		var result = Result.Success(value);

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.IsFailure.Should().BeFalse();
		result.Value.Should().Be(value);
		result.Error.Should().BeEmpty();
	}

	[Fact]
	public void GenericFailure_WithError_ShouldCreateFailureResult()
	{
		// Arrange
		var errorMessage = "Failed to get value";

		// Act
		var result = Result.Failure<string>(errorMessage);

		// Assert
		result.IsSuccess.Should().BeFalse();
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(errorMessage);
		var act = () => _ = result.Value;
		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void GenericSuccess_WithNullValue_ShouldStillBeSuccess()
	{
		// Act
		var result = Result.Success<string?>(null);

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeNull();
	}

	[Fact]
	public void GenericSuccess_WithComplexObject_ShouldStoreValue()
	{
		// Arrange
		var complexObject = new { Id = 1, Name = "Test" };

		// Act
		var result = Result.Success(complexObject);

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEquivalentTo(complexObject);
	}

	[Fact]
	public void MultipleFailures_ShouldPreserveErrorMessages()
	{
		// Arrange
		var error1 = "First error";
		var error2 = "Second error";

		// Act
		var result1 = Result.Failure(error1);
		var result2 = Result.Failure(error2);

		// Assert
		result1.Error.Should().Be(error1);
		result2.Error.Should().Be(error2);
	}
}