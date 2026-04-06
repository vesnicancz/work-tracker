namespace WorkTracker.Plugin.Abstractions;

public interface ITokenProvider
{
    Task<string?> AcquireTokenSilentAsync(CancellationToken cancellationToken);
    Task<string?> AcquireTokenInteractiveAsync(IProgress<string>? progress, CancellationToken cancellationToken);
}
