using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;
using static WorkTracker.Plugin.StreamDeck.RemoteControlProtocol;

namespace WorkTracker.Plugin.StreamDeck;

/// <summary>
/// Serves one client at a time on a named pipe restricted to the current user. The client is expected
/// to reconnect: it comes and goes with the Stream Deck app, and this server with the GUI.
/// </summary>
internal sealed class RemoteControlPipeServer(string pipeName, IRemoteControlHost host, Func<TimeSpan> lastWorkMaxAge, ILogger logger) : IAsyncDisposable
{
	/// <summary>
	/// Wait before opening the pipe again after a failure, typically a second running instance of the
	/// application holding it. Takes over within seconds when that instance exits.
	/// </summary>
	private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Commands are handled one at a time, so a suggestion plugin that hangs must not hold up the dial for
	/// long. Shorter than the timeout in the client, which then gets to hear why it failed.
	/// </summary>
	private static readonly TimeSpan SuggestionsTimeout = TimeSpan.FromSeconds(25);

	private readonly CancellationTokenSource _stopping = new();
	private Task? _serving;

	public void Start()
	{
		_serving = Task.Run(() => ServeAsync(_stopping.Token));
	}

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();

		if (_serving != null)
		{
			await _serving;
		}

		_stopping.Dispose();
	}

	private async Task ServeAsync(CancellationToken cancellationToken)
	{
		var failureLogged = false;

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await using var pipe = new NamedPipeServerStream(
					pipeName,
					PipeDirection.InOut,
					maxNumberOfServerInstances: 1,
					PipeTransmissionMode.Byte,
					PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

				await pipe.WaitForConnectionAsync(cancellationToken);
				failureLogged = false;
				logger.LogInformation("Stream Deck connected");

				await HandleConnectionAsync(pipe, cancellationToken);
				logger.LogInformation("Stream Deck disconnected");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				// Once per outage: this retries for as long as another instance holds the pipe.
				if (!failureLogged)
				{
					logger.LogWarning(ex, "Stream Deck pipe failed, retrying every {Delay}", RetryDelay);
					failureLogged = true;
				}

				try
				{
					await Task.Delay(RetryDelay, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}
	}

	private async Task HandleConnectionAsync(Stream pipe, CancellationToken cancellationToken)
	{
		using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using var writeLock = new SemaphoreSlim(1, 1);

		// Capacity of one: changes arriving while a push is on its way collapse into the next push,
		// which reads the state afresh anyway.
		var stateChanges = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
		{
			FullMode = BoundedChannelFullMode.DropWrite,
			SingleReader = true
		});

		void OnStateChanged(object? sender, EventArgs e) => stateChanges.Writer.TryWrite(true);

		host.StateChanged += OnStateChanged;
		stateChanges.Writer.TryWrite(true);
		var pushing = PushStateAsync(pipe, writeLock, stateChanges.Reader, connection.Token);
		var suggestionRequests = new List<Task>();

		try
		{
			using var reader = new StreamReader(pipe, leaveOpen: true);
			while (await reader.ReadLineAsync(cancellationToken) is { } line)
			{
				var request = ParseRequest(line);

				// Suggestions can take many seconds. A press in the meantime must not wait behind them:
				// the client would give up on it and report a failure, and it would still run later.
				// Responses carry the id of their request, so they may arrive in any order.
				if (request?.Command == Commands.GetSuggestions)
				{
					suggestionRequests.RemoveAll(task => task.IsCompleted);
					suggestionRequests.Add(RespondToSuggestionsAsync(pipe, writeLock, request, connection.Token));
					continue;
				}

				var response = await HandleRequestAsync(request, cancellationToken);
				await WriteAsync(pipe, writeLock, response, cancellationToken);
			}
		}
		finally
		{
			host.StateChanged -= OnStateChanged;
			await connection.CancelAsync();
			await Task.WhenAll(suggestionRequests);
			await pushing;
		}
	}

	private async Task RespondToSuggestionsAsync(Stream pipe, SemaphoreSlim writeLock, RemoteRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var response = await HandleRequestAsync(request, cancellationToken);
			await WriteAsync(pipe, writeLock, response, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			// The connection is over
		}
		catch (IOException ex)
		{
			// The read loop sees the broken pipe too and ends the connection
			logger.LogDebug(ex, "Stream Deck went away while sending suggestions");
		}
	}

	private async Task PushStateAsync(Stream pipe, SemaphoreSlim writeLock, ChannelReader<bool> stateChanges, CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var _ in stateChanges.ReadAllAsync(cancellationToken))
			{
				try
				{
					var state = await host.GetStateAsync(lastWorkMaxAge(), cancellationToken);
					await WriteAsync(pipe, writeLock, RemoteStateMessage.From(state), cancellationToken);
				}
				catch (Exception ex) when (ex is not (OperationCanceledException or IOException))
				{
					// The client keeps showing the last state; the next change tries again.
					logger.LogWarning(ex, "Failed to send the state to Stream Deck");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// The connection is over
		}
		catch (IOException ex)
		{
			// The read loop sees the broken pipe too and ends the connection
			logger.LogDebug(ex, "Stream Deck went away while sending the state");
		}
	}

	private RemoteRequest? ParseRequest(string line)
	{
		try
		{
			return JsonSerializer.Deserialize<RemoteRequest>(line, JsonOptions);
		}
		catch (JsonException ex)
		{
			logger.LogWarning(ex, "Received malformed request from Stream Deck");
			return null;
		}
	}

	private async Task<RemoteResponse> HandleRequestAsync(RemoteRequest? request, CancellationToken cancellationToken)
	{
		if (request == null)
		{
			return new RemoteResponse(null, false, "Malformed request.");
		}

		try
		{
			if (request.Command == Commands.GetSuggestions)
			{
				return new RemoteResponse(request.Id, true, null, await GetSuggestionsAsync(cancellationToken));
			}

			if (request.Command == Commands.GetFavorites)
			{
				return new RemoteResponse(request.Id, true, null, Favorites: host.GetFavorites());
			}

			var result = await ExecuteAsync(request, cancellationToken);
			return new RemoteResponse(request.Id, result.IsSuccess, result.Error);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Stream Deck command {Command} failed", request.Command);
			return new RemoteResponse(request.Id, false, ex.Message);
		}
	}

	private async Task<RemoteSuggestions> GetSuggestionsAsync(CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(SuggestionsTimeout);

		return await host.GetSuggestionsAsync(timeout.Token);
	}

	private Task<PluginResult> ExecuteAsync(RemoteRequest request, CancellationToken cancellationToken) => request.Command switch
	{
		Commands.ToggleWork => ToggleWorkAsync(cancellationToken),
		Commands.StartWork => host.StartWorkAsync(request.TicketId, request.Description, cancellationToken),
		Commands.StopWork => host.StopWorkAsync(cancellationToken),
		Commands.ResumeWork => host.ResumeLastWorkAsync(lastWorkMaxAge(), cancellationToken),
		Commands.ShiftStart => host.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(request.Minutes ?? 0), cancellationToken),
		Commands.AddWork when request.StartTime is { } startTime => host.AddWorkEntryAsync(request.TicketId, request.Description, startTime, request.EndTime, cancellationToken),
		Commands.AddWork => Task.FromResult(PluginResult.Failure("The start time is missing.", PluginErrorCategory.Validation)),
		Commands.ShowNewWorkDialog => host.ShowNewWorkEntryDialogAsync(request.TicketId, request.Description, cancellationToken),
		Commands.StartPomodoro => host.StartPomodoroAsync(cancellationToken),
		Commands.StopPomodoro => host.StopPomodoroAsync(cancellationToken),
		Commands.SkipPomodoro => host.SkipPomodoroPhaseAsync(cancellationToken),
		_ => Task.FromResult(PluginResult.Failure($"Unknown command '{request.Command}'.", PluginErrorCategory.Validation))
	};

	/// <summary>
	/// Decided here rather than in the client, whose view of the state can be a moment behind: a press
	/// must not restart work that was stopped elsewhere a moment ago.
	/// </summary>
	private async Task<PluginResult> ToggleWorkAsync(CancellationToken cancellationToken)
	{
		var maxAge = lastWorkMaxAge();
		var state = await host.GetStateAsync(maxAge, cancellationToken);

		return state.ActiveWork != null
			? await host.StopWorkAsync(cancellationToken)
			: await host.ResumeLastWorkAsync(maxAge, cancellationToken);
	}

	private static async Task WriteAsync<T>(Stream pipe, SemaphoreSlim writeLock, T message, CancellationToken cancellationToken)
	{
		byte[] line = [.. JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions), (byte)'\n'];

		await writeLock.WaitAsync(cancellationToken);
		try
		{
			await pipe.WriteAsync(line, cancellationToken);
			await pipe.FlushAsync(cancellationToken);
		}
		finally
		{
			writeLock.Release();
		}
	}
}
