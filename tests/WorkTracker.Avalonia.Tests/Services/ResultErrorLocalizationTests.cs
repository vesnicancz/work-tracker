using FluentAssertions;
using WorkTracker.Application.Common;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class ResultErrorLocalizationTests
{
	public static TheoryData<string> Languages => new("cs", "en");

	private static readonly ResultError[] AllErrors =
	[
		WorkEntryErrors.InvalidEntry,
		WorkEntryErrors.InvalidEntryAfterUpdate,
		WorkEntryErrors.Overlap,
		WorkEntryErrors.PlanOutdated,
		WorkEntryErrors.NoActiveWork,
		WorkEntryErrors.InvalidEndTime,
		WorkEntryErrors.NotFound(1)
	];

	[Theory]
	[MemberData(nameof(Languages))]
	public void EveryWorkEntryError_HasATranslation(string language)
	{
		var localization = new LocalizationService();
		localization.ApplyLanguage(language);

		foreach (var error in AllErrors)
		{
			var text = localization.LocalizeError(Result.Failure(error));

			text.Should().NotBeNullOrWhiteSpace();
			text.Should().NotContain($"Error{error.Code}", "the resource should exist");
		}
	}

	[Fact]
	public void WorkEntryError_IsShownInTheLanguageOfTheApplication()
	{
		var localization = new LocalizationService();
		localization.ApplyLanguage("cs");

		var text = localization.LocalizeError(Result.Failure(WorkEntryErrors.Overlap));

		text.Should().NotBe(WorkEntryErrors.Overlap.Message);
		text.Should().Contain("překrývá");
	}

	[Fact]
	public void FailureWithoutACode_KeepsItsText()
	{
		var localization = new LocalizationService();

		localization.LocalizeError(Result.Failure("Unexpected error: boom")).Should().Be("Unexpected error: boom");
	}

	[Fact]
	public void PassedOnFailure_KeepsItsCode()
	{
		var passedOn = Result.Failure<bool>(Result.Failure<int>(WorkEntryErrors.Overlap));

		passedOn.ErrorDetail.Should().Be(WorkEntryErrors.Overlap);
		passedOn.Error.Should().Be(WorkEntryErrors.Overlap.Message);
	}
}
