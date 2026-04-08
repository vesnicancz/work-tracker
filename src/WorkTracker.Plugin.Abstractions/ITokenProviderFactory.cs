namespace WorkTracker.Plugin.Abstractions;

public interface ITokenProviderFactory
{
	Task<ITokenProvider> CreateAsync(string tenantId, string clientId, string[] scopes);
}
