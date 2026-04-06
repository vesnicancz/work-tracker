using Microsoft.Extensions.Logging;

namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Common base class for all plugin types providing configuration, validation, logging, and lifecycle management.
/// </summary>
public abstract class PluginBase(ILogger logger) : IPlugin
{
	protected ILogger Logger { get; } = logger;

	protected IDictionary<string, string> Configuration { get; private set; } = new Dictionary<string, string>();

	protected bool IsInitialized { get; private set; }

	public abstract PluginMetadata Metadata { get; }

	public abstract IReadOnlyList<PluginConfigurationField> GetConfigurationFields();

	public virtual async Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default)
	{
		var previousConfiguration = Configuration;
		Configuration = configuration ?? new Dictionary<string, string>();

		var validationResult = await ValidateConfigurationAsync(Configuration, cancellationToken);
		if (!validationResult.IsValid)
		{
			Logger.LogError(
				"Plugin {Name} configuration validation failed: {Errors}",
				Metadata.Name,
				string.Join(", ", validationResult.Errors)
			);
			if (IsInitialized)
			{
				Configuration = previousConfiguration;
			}
			return false;
		}

		var result = await OnInitializeAsync(Configuration, cancellationToken);

		if (result)
		{
			IsInitialized = true;
			Logger.LogInformation("Plugin {Name} initialized successfully", Metadata.Name);
		}
		else
		{
			if (IsInitialized)
			{
				Configuration = previousConfiguration;
				Logger.LogWarning("Plugin {Name} re-initialization failed, keeping previous state", Metadata.Name);
			}
			else
			{
				Logger.LogError("Plugin {Name} initialization failed", Metadata.Name);
			}
		}

		return result;
	}

	public virtual async Task ShutdownAsync()
	{
		await OnShutdownAsync();
		IsInitialized = false;
		Logger.LogInformation("Plugin {Name} shut down", Metadata.Name);
		await DisposeAsync();
	}

	/// <inheritdoc />
	public virtual ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);
		return ValueTask.CompletedTask;
	}

	public virtual async Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		var errors = new List<string>();

		foreach (var field in GetConfigurationFields().Where(f => f.IsRequired))
		{
			if (!configuration.ContainsKey(field.Key) || string.IsNullOrWhiteSpace(configuration[field.Key]))
			{
				errors.Add($"Required field '{field.Label}' is missing or empty");
			}
			else if (!string.IsNullOrEmpty(field.ValidationPattern))
			{
				var value = configuration[field.Key];
				try
				{
					if (!System.Text.RegularExpressions.Regex.IsMatch(value, field.ValidationPattern,
						System.Text.RegularExpressions.RegexOptions.None,
						TimeSpan.FromSeconds(1)))
					{
						errors.Add(field.ValidationMessage ?? $"Field '{field.Label}' has invalid format");
					}
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
				{
					Logger.LogError(ex, "Regex validation for field '{FieldKey}' in plugin {PluginName} timed out.",
						field.Key, Metadata.Name);
					errors.Add($"Field '{field.Label}' validation timed out; the validation pattern may be too complex or the input too long.");
				}
			}
		}

		if (errors.Count != 0)
		{
			return PluginValidationResult.Failure(errors.ToArray());
		}

		return await OnValidateConfigurationAsync(configuration, cancellationToken);
	}

	protected string? GetConfigValue(string key)
	{
		return Configuration.TryGetValue(key, out var value) ? value : null;
	}

	protected string GetRequiredConfigValue(string key)
	{
		if (!Configuration.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Required configuration key '{key}' is missing");
		}
		return value;
	}

	protected void EnsureInitialized()
	{
		if (!IsInitialized)
		{
			throw new InvalidOperationException($"Plugin {Metadata.Name} is not initialized");
		}
	}

	protected virtual Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		return Task.FromResult(true);
	}

	protected virtual Task OnShutdownAsync()
	{
		return Task.CompletedTask;
	}

	protected virtual Task<PluginValidationResult> OnValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginValidationResult.Success());
	}
}
