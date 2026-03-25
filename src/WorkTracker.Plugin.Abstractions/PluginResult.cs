namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Represents the result of a plugin operation
/// </summary>
public class PluginResult
{
	/// <summary>
	/// Indicates whether the operation was successful
	/// </summary>
	public bool IsSuccess { get; init; }

	/// <summary>
	/// Indicates whether the operation failed
	/// </summary>
	public bool IsFailure => !IsSuccess;

	/// <summary>
	/// Error message if the operation failed
	/// </summary>
	public string? Error { get; init; }

	protected PluginResult(bool isSuccess, string? error = null)
	{
		IsSuccess = isSuccess;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result
	/// </summary>
	public static PluginResult Success() => new(true);

	/// <summary>
	/// Creates a failed result with an error message
	/// </summary>
	public static PluginResult Failure(string error) => new(false, error);
}

/// <summary>
/// Represents the result of a plugin operation with a value
/// </summary>
public class PluginResult<T> : PluginResult
{
	/// <summary>
	/// The result value (only available if successful)
	/// </summary>
	public T? Value { get; init; }

	private PluginResult(bool isSuccess, T? value = default, string? error = null)
		: base(isSuccess, error)
	{
		Value = value;
	}

	/// <summary>
	/// Creates a successful result with a value
	/// </summary>
	public static PluginResult<T> Success(T value) => new(true, value);

	/// <summary>
	/// Creates a failed result with an error message
	/// </summary>
	public new static PluginResult<T> Failure(string error) => new(false, default, error);
}
