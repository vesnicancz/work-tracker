using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace WorkTracker.Plugin.Atlassian;

/// <summary>
/// Shared Jira REST API client with Basic authentication.
/// Used by both TempoWorklogPlugin and JiraSuggestionsPlugin.
/// </summary>
internal sealed class JiraClient(HttpClient httpClient, string baseUrl) : IJiraClient
{
	private readonly HttpClient _httpClient = httpClient;
	private bool _disposed;

	public string BaseUrl { get; } = baseUrl.TrimEnd('/');

	internal static JiraClient Create(IHttpClientFactory httpClientFactory, string baseUrl, string email, string apiToken)
	{
		var httpClient = httpClientFactory.CreateClient();
		httpClient.Timeout = TimeSpan.FromSeconds(30);
		var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
		httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
		httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		return new JiraClient(httpClient, baseUrl);
	}

	public Task<HttpResponseMessage> GetAsync(string relativeUrl, CancellationToken cancellationToken)
	{
		return _httpClient.GetAsync($"{BaseUrl}{relativeUrl}", cancellationToken);
	}

	public async Task<JsonElement> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
	{
		using var response = await GetAsync(relativeUrl, cancellationToken);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
	}

	public async Task<(bool Success, string? Error, int? StatusCode)> TestConnectionAsync(CancellationToken cancellationToken)
	{
		try
		{
			using var response = await GetAsync("/rest/api/3/myself", cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				return (true, null, (int)response.StatusCode);
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			return (false, $"Jira returned {(int)response.StatusCode}: {body}", (int)response.StatusCode);
		}
		catch (Exception ex)
		{
			return (false, $"Connection failed: {ex.Message}", null);
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
