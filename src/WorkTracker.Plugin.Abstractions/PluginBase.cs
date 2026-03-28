using Microsoft.Extensions.Logging;

namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Common base class for all plugin types providing configuration, validation, logging, and lifecycle management.
/// </summary>
public abstract class PluginBase : IPlugin
{
	protected ILogger? Logger { get; private set; }

	protected IDictionary<string, string> Configuration { get; private set; } = new Dictionary<string, string>();

	protected bool IsInitialized { get; private set; }

	public abstract PluginMetadata Metadata { get; }

	public abstract IReadOnlyList<PluginConfigurationField> GetConfigurationFields();

	public virtual async Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default)
	{
		Configuration = configuration ?? new Dictionary<string, string>();

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

	public virtual async Task ShutdownAsync()
	{
		await OnShutdownAsync();
		IsInitialized = false;
		Logger?.LogInformation("Plugin {Name} shut down", Metadata.Name);
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
				if (!System.Text.RegularExpressions.Regex.IsMatch(value, field.ValidationPattern,
					System.Text.RegularExpressions.RegexOptions.None,
					TimeSpan.FromSeconds(1)))
				{
					errors.Add(field.ValidationMessage ?? $"Field '{field.Label}' has invalid format");
				}
			}
		}

		if (errors.Count != 0)
		{
			return PluginValidationResult.Failure(errors.ToArray());
		}

		return await OnValidateConfigurationAsync(configuration, cancellationToken);
	}

	public void SetLogger(ILogger logger)
	{
		Logger = logger;
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
