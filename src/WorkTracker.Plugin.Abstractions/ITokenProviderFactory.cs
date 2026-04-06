namespace WorkTracker.Plugin.Abstractions;

public interface ITokenProviderFactory
{
    ITokenProvider Create(string tenantId, string clientId, string[] scopes);
}
