using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Checks GitHub releases for a newer application version and notifies the user.
/// </summary>
public sealed class UpdateCheckService : IUpdateCheckService
{
	private const string GitHubOwner = "vesnicancz";
	private const string GitHubRepo = "work-tracker";
	private const string ReleasesApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
	private const string ReleasesFallbackUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest";

	private readonly string _currentVersion;
	private readonly HttpClient _httpClient;
	private readonly ISettingsService _settingsService;
	private readonly ISystemNotificationService _systemNotification;
	private readonly ILocalizationService _localization;
	private readonly ILogger<UpdateCheckService> _logger;

	public UpdateCheckService(
		string currentVersion,
		HttpClient httpClient,
		ISettingsService settingsService,
		ISystemNotificationService systemNotification,
		ILocalizationService localization,
		ILogger<UpdateCheckService> logger)
	{
		_currentVersion = currentVersion;
		_httpClient = httpClient;
		_settingsService = settingsService;
		_systemNotification = systemNotification;
		_localization = localization;
		_logger = logger;
	}

	public async Task CheckForUpdateAsync(CancellationToken cancellationToken)
	{
		try
		{
			if (!_settingsService.Settings.CheckForUpdates)
			{
				_logger.LogDebug("Update check is disabled in settings");
				return;
			}

			var localVersion = ParseVersion(_currentVersion);
			if (localVersion == null)
			{
				_logger.LogWarning("Could not parse current app version: {Version}", _currentVersion);
				return;
			}

			using var response = await _httpClient.GetAsync(ReleasesApiUrl, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				_logger.LogDebug("GitHub API returned {StatusCode}", response.StatusCode);
				return;
			}

			using var json = await JsonDocument.ParseAsync(
				await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

			var root = json.RootElement;
			var tagName = root.GetProperty("tag_name").GetString();
			var htmlUrl = root.TryGetProperty("html_url", out var urlProp)
				? urlProp.GetString()
				: ReleasesFallbackUrl;

			if (string.IsNullOrEmpty(tagName))
			{
				return;
			}

			var remoteVersion = ParseVersion(tagName);
			if (remoteVersion == null)
			{
				_logger.LogDebug("Could not parse remote version from tag: {TagName}", tagName);
				return;
			}

			if (remoteVersion <= localVersion)
			{
				_logger.LogDebug("App is up to date ({Local} >= {Remote})", localVersion, remoteVersion);
				return;
			}

			_logger.LogInformation("New version available: {Remote} (current: {Local})", remoteVersion, localVersion);

			var title = _localization["UpdateAvailableTitle"];
			var message = _localization.GetFormattedString("UpdateAvailableMessage", tagName, _currentVersion);
			await _systemNotification.ShowNotificationAsync(title, message, htmlUrl);
		}
		catch (OperationCanceledException)
		{
			// Expected during shutdown
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Update check failed (this is expected when offline)");
		}
	}

	internal static Version? ParseVersion(string versionString)
	{
		var cleaned = versionString.TrimStart('v', 'V');

		var dashIndex = cleaned.IndexOf('-');
		if (dashIndex >= 0)
		{
			cleaned = cleaned[..dashIndex];
		}

		var plusIndex = cleaned.IndexOf('+');
		if (plusIndex >= 0)
		{
			cleaned = cleaned[..plusIndex];
		}

		return Version.TryParse(cleaned, out var version) ? version : null;
	}
}