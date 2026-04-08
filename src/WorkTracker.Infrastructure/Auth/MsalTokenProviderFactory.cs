using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using WorkTracker.Application;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Auth;

public sealed class MsalTokenProviderFactory(ILoggerFactory loggerFactory) : ITokenProviderFactory
{
	public async Task<ITokenProvider> CreateAsync(string tenantId, string clientId, string[] scopes)
	{
		var msalApp = PublicClientApplicationBuilder
			.Create(clientId)
			.WithAuthority($"https://login.microsoftonline.com/{tenantId}")
			.WithDefaultRedirectUri()
			.Build();

		// Hash tenantId:clientId to produce a safe filename (prevents path traversal via user-provided values)
		var safeKey = Convert.ToHexString(
			SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}:{clientId}")));
		var cacheFileName = $"msal_{safeKey}.bin";

		var storageProperties = new StorageCreationPropertiesBuilder(cacheFileName, WorkTrackerPaths.MsalCacheDirectory)
			.WithMacKeyChain("WorkTracker", $"msal-{safeKey}")
			.WithLinuxKeyring("worktracker", "default", "MSAL token cache",
				new KeyValuePair<string, string>("app", "worktracker"),
				new KeyValuePair<string, string>("cache", safeKey))
			.Build();

		var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
		cacheHelper.RegisterCache(msalApp.UserTokenCache);

		var logger = loggerFactory.CreateLogger<MsalTokenProvider>();
		return new MsalTokenProvider(msalApp, scopes, logger);
	}
}
