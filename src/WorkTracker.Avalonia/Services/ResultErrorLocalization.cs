using WorkTracker.Application.Common;

namespace WorkTracker.Avalonia.Services;

public static class ResultErrorLocalization
{
	/// <summary>
	/// The failure in the language of the application. A failure without a code, such as an unexpected
	/// exception, only has its original text.
	/// </summary>
	public static string LocalizeError(this ILocalizationService localization, Result result)
	{
		return result.ErrorDetail is { } error
			? localization.GetFormattedString($"Error{error.Code}", error.Args)
			: result.Error;
	}
}
