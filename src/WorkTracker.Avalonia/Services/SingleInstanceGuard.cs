using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Keeps one running GUI per data directory. Autostart followed by a click on the shortcut would
/// otherwise run two applications side by side, each with its own tray icon and its own idea of the
/// hotkey, the Stream Deck pipe and the LED. A second start asks the running one to show itself.
/// <para>
/// An open file rather than a named mutex: it goes away with the process however that ends, it works
/// the same on every platform, and going by the directory keeps a Development run from getting in the
/// way of the installed application.
/// </para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
	private const string LockFileName = "instance.lock";

	private readonly FileStream _lockFile;
	private readonly string _pipeName;
	private readonly CancellationTokenSource _cts = new();

	private SingleInstanceGuard(FileStream lockFile, string pipeName)
	{
		_lockFile = lockFile;
		_pipeName = pipeName;
	}

	/// <summary>
	/// Returns null when another instance holds the directory.
	/// </summary>
	public static SingleInstanceGuard? TryAcquire(string dataDirectory)
	{
		Directory.CreateDirectory(dataDirectory);

		try
		{
			var lockFile = new FileStream(Path.Combine(dataDirectory, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			return new SingleInstanceGuard(lockFile, PipeNameFor(dataDirectory));
		}
		catch (IOException)
		{
			return null;
		}
	}

	/// <summary>
	/// Asks the instance holding the directory to show itself. False when it didn't answer, which is
	/// also what happens while it is still starting up or already shutting down.
	/// </summary>
	public static bool SignalRunningInstance(string dataDirectory, TimeSpan timeout)
	{
		try
		{
			using var client = new NamedPipeClientStream(".", PipeNameFor(dataDirectory), PipeDirection.Out, PipeOptions.CurrentUserOnly);
			client.Connect(timeout);
			return true;
		}
		catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>
	/// Calls <paramref name="onActivationRequested"/> on a thread pool thread for each later start.
	/// </summary>
	public void Listen(Action onActivationRequested)
	{
		_ = ListenAsync(onActivationRequested, _cts.Token);
	}

	private async Task ListenAsync(Action onActivationRequested, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await using var server = new NamedPipeServerStream(
					_pipeName,
					PipeDirection.In,
					1,
					PipeTransmissionMode.Byte,
					PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

				await server.WaitForConnectionAsync(cancellationToken);
				onActivationRequested();
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
			{
				// A later start just doesn't get the window to show. Nothing here is worth stopping the
				// application for, and retrying right away could only spin.
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}
	}

	// Short, because on Unix the name ends up in a socket path with a tight length limit
	private static string PipeNameFor(string dataDirectory)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory)));
		return $"worktracker-{Convert.ToHexStringLower(hash)[..16]}";
	}

	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
		_lockFile.Dispose();
	}
}
