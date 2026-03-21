using Microsoft.Extensions.Logging;

namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Base class for worklog upload plugins providing common functionality
/// </summary>
public abstract class WorklogUploadPluginBase : IWorklogUploadPlugin
{
	protected ILogger? Logger { get; private set; }

	protected IDictionary<string, string> Configuration { get; private set; } = new Dictionary<string, string>();

	protected bool IsInitialized { get; private set; }

	public abstract PluginMetadata Metadata { get; }

	/// <summary>
	/// Gets the configuration fields required by this plugin
	/// </summary>
	public abstract IReadOnlyList<PluginConfigurationField> GetConfigurationFields();

	/// <summary>
	/// Initializes the plugin with configuration
	/// </summary>
	public virtual async Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default)
	{
		Configuration = configuration ?? new Dictionary<string, string>();

		// Validate configuration
		var validationResult = await ValidateConfigurationAsync(Configuration, cancellationToken);
		if (!validationResult.IsValid)
		{
			Logger?.LogError(
				"Plugin {Name} configuration validation failed: {Errors}",
				Metadata.Name,
				string.Join(", ", validationResult.Errors)
			);
			return false;
		}

		// Call derived class initialization
		var result = await OnInitializeAsync(Configuration, cancellationToken);
		IsInitialized = result;

		if (result)
		{
			Logger?.LogInformation("Plugin {Name} initialized successfully", Metadata.Name);
		}
		else
		{
			Logger?.LogError("Plugin {Name} initialization failed", Metadata.Name);
		}

		return result;
	}

	/// <summary>
	/// Called when the plugin is being unloaded
	/// </summary>
	public virtual async Task ShutdownAsync()
	{
		await OnShutdownAsync();
		IsInitialized = false;
		Logger?.LogInformation("Plugin {Name} shut down", Metadata.Name);
	}

	/// <summary>
	/// Validates the plugin configuration
	/// </summary>
	public virtual async Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		var errors = new List<string>();

		// Validate required fields
		foreach (var field in GetConfigurationFields().Where(f => f.IsRequired))
		{
			if (!configuration.ContainsKey(field.Key) || string.IsNullOrWhiteSpace(configuration[field.Key]))
			{
				errors.Add($"Required field '{field.Label}' is missing or empty");
			}
			else if (!string.IsNullOrEmpty(field.ValidationPattern))
			{
				var value = configuration[field.Key];
				if (!System.Text.RegularExpressions.Regex.IsMatch(value, field.ValidationPattern))
				{
					errors.Add(field.ValidationMessage ?? $"Field '{field.Label}' has invalid format");
				}
			}
		}

		if (errors.Count != 0)
		{
			return PluginValidationResult.Failure(errors.ToArray());
		}

		// Call derived class validation
		return await OnValidateConfigurationAsync(configuration, cancellationToken);
	}

	/// <summary>
	/// Tests the connection with the current configuration
	/// </summary>
	public abstract Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Uploads a single worklog entry
	/// </summary>
	public abstract Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken);

	/// <summary>
	/// Uploads multiple worklog entries in batch
	/// </summary>
	public virtual async Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, CancellationToken cancellationToken)
	{
		if (!IsInitialized)
		{
			return PluginResult<WorklogSubmissionResult>.Failure("Plugin is not initialized");
		}

		var worklogList = worklogs.ToList();
		var successful = 0;
		var failed = 0;
		var errors = new List<WorklogSubmissionError>();

		foreach (var worklog in worklogList)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var result = await UploadWorklogAsync(worklog, cancellationToken);
			if (result.IsSuccess)
			{
				successful++;
			}
			else
			{
				failed++;
				errors.Add(new WorklogSubmissionError
				{
					Worklog = worklog,
					ErrorMessage = result.Error ?? "Unknown error"
				});
			}
		}

		var submissionResult = new WorklogSubmissionResult
		{
			TotalEntries = worklogList.Count,
			SuccessfulEntries = successful,
			FailedEntries = failed,
			Errors = errors
		};

		return PluginResult<WorklogSubmissionResult>.Success(submissionResult);
	}

	/// <summary>
	/// Gets existing worklogs from the provider for a specific date range
	/// </summary>
	public abstract Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken);

	/// <summary>
	/// Checks if a worklog already exists in the provider
	/// </summary>
	public abstract Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken);

	/// <summary>
	/// Sets the logger for this plugin
	/// </summary>
	public void SetLogger(ILogger logger)
	{
		Logger = logger;
	}

	/// <summary>
	/// Gets a configuration value by key
	/// </summary>
	protected string? GetConfigValue(string key)
	{
		return Configuration.TryGetValue(key, out var value) ? value : null;
	}

	/// <summary>
	/// Gets a required configuration value by key
	/// </summary>
	protected string GetRequiredConfigValue(string key)
	{
		if (!Configuration.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Required configuration key '{key}' is missing");
		}
		return value;
	}

	/// <summary>
	/// Ensures the plugin is initialized before performing operations
	/// </summary>
	protected void EnsureInitialized()
	{
		if (!IsInitialized)
		{
			throw new InvalidOperationException($"Plugin {Metadata.Name} is not initialized");
		}
	}

	// Virtual methods for derived classes to override

	/// <summary>
	/// Called during initialization. Override to perform custom initialization logic.
	/// </summary>
	protected virtual Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		return Task.FromResult(true);
	}

	/// <summary>
	/// Called during shutdown. Override to perform custom cleanup logic.
	/// </summary>
	protected virtual Task OnShutdownAsync()
	{
		return Task.CompletedTask;
	}

	/// <summary>
	/// Called during validation. Override to perform custom validation logic.
	/// </summary>
	protected virtual Task<PluginValidationResult> OnValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginValidationResult.Success());
	}
}
