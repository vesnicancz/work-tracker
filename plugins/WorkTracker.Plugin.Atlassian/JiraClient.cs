using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace WorkTracker.Plugin.Atlassian;

/// <summary>
/// Shared Jira REST API client with Basic authentication.
/// Used by both TempoWorklogPlugin and JiraSuggestionsPlugin.
/// </summary>
internal sealed class JiraClient : IDisposable
{
	private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

	private readonly HttpClient _httpClient;
	private bool _disposed;

	public string BaseUrl { get; }

	public JiraClient(string baseUrl, string email, string apiToken)
	{
		BaseUrl = baseUrl.TrimEnd('/');

		_httpClient = new HttpClient { Timeout = HttpTimeout };

		var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public Task<HttpResponseMessage> GetAsync(string relativeUrl, CancellationToken cancellationToken)
	{
		return _httpClient.GetAsync($"{BaseUrl}{relativeUrl}", cancellationToken);
	}

	public async Task<JsonElement> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
	{
		var response = await GetAsync(relativeUrl, cancellationToken);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
	}

	public async Task<(bool Success, string? Error)> TestConnectionAsync(CancellationToken cancellationToken)
	{
		try
		{
			var response = await GetAsync("/rest/api/3/myself", cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				return (true, null);
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			return (false, $"Jira returned {(int)response.StatusCode}: {body}");
		}
		catch (Exception ex)
		{
			return (false, $"Connection failed: {ex.Message}");
		}
	}

	public async Task<string?> GetCurrentUserAccountIdAsync(CancellationToken cancellationToken)
	{
		var json = await GetJsonAsync("/rest/api/3/myself", cancellationToken);
		return json.TryGetProperty("accountId", out var prop) ? prop.GetString() : null;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_httpClient.Dispose();
			_disposed = true;
		}
	}
}