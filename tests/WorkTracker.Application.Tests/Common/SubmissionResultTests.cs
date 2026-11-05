using FluentAssertions;
using WorkTracker.Application.Common;

namespace WorkTracker.Application.Tests.Common;

public class SubmissionResultTests
{
	private static readonly SubmissionError Error = new() { ErrorMessage = "failed" };

	[Fact]
	public void NothingFailed_HasNoFailures()
	{
		var result = new SubmissionResult { TotalEntries = 5, SuccessfulEntries = 5 };

		result.HasFailures.Should().BeFalse();
		result.FailureCount.Should().Be(0);
	}

	[Fact]
	public void NothingSubmitted_HasNoFailures()
	{
		new SubmissionResult().HasFailures.Should().BeFalse();
	}

	[Fact]
	public void FailedEntries_AreFailures()
	{
		var result = new SubmissionResult { TotalEntries = 5, SuccessfulEntries = 3, FailedEntries = 2, Errors = [Error, Error] };

		result.HasFailures.Should().BeTrue();
		result.FailureCount.Should().Be(2);
	}

	[Fact]
	public void ErrorWithoutAFailedEntry_IsAFailure()
	{
		var result = new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1, Errors = [Error] };

		result.HasFailures.Should().BeTrue();
		result.FailureCount.Should().Be(1);
	}

	[Fact]
	public void FailedEntryAndAnErrorOfItsOwn_CountsBoth()
	{
		var result = new SubmissionResult { TotalEntries = 2, SuccessfulEntries = 1, FailedEntries = 1, Errors = [Error, Error] };

		result.FailureCount.Should().Be(2);
	}
}
