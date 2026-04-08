using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using WorkTracker.Application;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Auth;

public sealed class MsalTokenProviderFactory(ILoggerFactory loggerFactory) : ITokenProviderFactory
{
	private static readonly ConcurrentDictionary<string, Lock> _cacheLocks = new();

	private static Lock GetCacheLock(string cacheFilePath) =>
		_cacheLocks.GetOrAdd(cacheFilePath, _ => new Lock());

	public ITokenProvider Create(string tenantId, string clientId, string[] scopes)
	{
		var msalApp = PublicClientApplicationBuilder
			.Create(clientId)
			.WithAuthority($"https://login.microsoftonline.com/{tenantId}")
			.WithDefaultRedirectUri()
			.Build();

		// Use hash-based filename to prevent path traversal via user-provided tenantId/clientId
		var safeKey = Convert.ToHexString(
			SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}:{clientId}")));
		var cacheFilePath = WorkTrackerPaths.MsalCachePath(safeKey);

		Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);

		var logger = loggerFactory.CreateLogger<MsalTokenProvider>();

		msalApp.UserTokenCache.SetBeforeAccess(args =>
		{
			lock (GetCacheLock(cacheFilePath))
			{
				try
				{
					if (File.Exists(cacheFilePath))
					{
						args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(cacheFilePath));
					}
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to read MSAL token cache, proceeding with empty cache");
				}
			}
		});

		msalApp.UserTokenCache.SetAfterAccess(args =>
		{
			if (!args.HasStateChanged)
			{
				return;
			}

			lock (GetCacheLock(cacheFilePath))
			{
				try
				{
					File.WriteAllBytes(cacheFilePath, args.TokenCache.SerializeMsalV3());
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to write MSAL token cache");
				}
			}
		});

		return new MsalTokenProvider(msalApp, scopes, logger);
	}
}
