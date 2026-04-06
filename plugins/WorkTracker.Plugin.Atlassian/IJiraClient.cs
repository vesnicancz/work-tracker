using System.Text.Json;

namespace WorkTracker.Plugin.Atlassian;

/// <summary>
/// Shared Jira REST API client interface.
/// </summary>
public interface IJiraClient : IDisposable
{
	string BaseUrl { get; }
	Task<HttpResponseMessage> GetAsync(string relativeUrl, CancellationToken cancellationToken);
	Task<JsonElement> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken);
	Task<(bool Success, string? Error)> TestConnectionAsync(CancellationToken cancellationToken);
	Task<string?> GetCurrentUserAccountIdAsync(CancellationToken cancellationToken);
}
