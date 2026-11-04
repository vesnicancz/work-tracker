namespace WorkTracker.Application.Common;

/// <summary>
/// A failure a UI can translate: <paramref name="Code"/> identifies it, <paramref name="Args"/> fill
/// the translated text, and <paramref name="Message"/> is the English text.
/// </summary>
public sealed record ResultError(string Code, string Message, params object[] Args);

/// <summary>
/// Represents the result of an operation that can succeed or fail
/// </summary>
public class Result
{
	public bool IsSuccess { get; }

	public bool IsFailure => !IsSuccess;

	public string Error { get; }

	/// <summary>
	/// The failure in translatable form, or null when there is only the text in <see cref="Error"/>.
	/// </summary>
	public ResultError? ErrorDetail { get; private init; }

	protected Result(bool isSuccess, string error)
	{
		if (isSuccess && !string.IsNullOrEmpty(error))
		{
			throw new InvalidOperationException("Successful result cannot have an error message");
		}

		if (!isSuccess && string.IsNullOrEmpty(error))
		{
			throw new InvalidOperationException("Failed result must have an error message");
		}

		IsSuccess = isSuccess;
		Error = error ?? string.Empty;
	}

	public static Result Success() => new Result(true, string.Empty);

	public static Result Failure(string error) => new Result(false, error);

	public static Result Failure(ResultError error) => new Result(false, error.Message) { ErrorDetail = error };

	public static Result<T> Success<T>(T value) => new Result<T>(value, true, string.Empty);

	public static Result<T> Failure<T>(string error) => new Result<T>(default!, false, error);

	public static Result<T> Failure<T>(ResultError error) => new Result<T>(default!, false, error.Message) { ErrorDetail = error };

	/// <summary>
	/// Passes on the failure of another operation.
	/// </summary>
	public static Result<T> Failure<T>(Result failure) => new Result<T>(default!, false, failure.Error) { ErrorDetail = failure.ErrorDetail };
}

/// <summary>
/// Represents the result of an operation that returns a value
/// </summary>
public class Result<T> : Result
{
	private readonly T _value;

	public T Value => IsSuccess
		? _value
		: throw new InvalidOperationException($"Cannot access Value on a failed result. Error: {Error}");

	protected internal Result(T value, bool isSuccess, string error)
		: base(isSuccess, error)
	{
		_value = value;
	}
}
