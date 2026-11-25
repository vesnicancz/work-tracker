using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WorkTracker.Plugin.Mcp;

/// <summary>
/// Serves MCP over Streamable HTTP on <c>http://localhost:{port}/mcp</c>, in stateless mode: every POST
/// gets a server of its own, so there are no sessions to keep and nothing to send outside a response.
/// Built on <see cref="HttpListener"/> rather than ASP.NET Core, which a plugin can't bring along.
/// </summary>
internal sealed class McpHttpServer : IAsyncDisposable
{
	public const string Path = "/mcp";

	private static readonly JsonTypeInfo<JsonRpcMessage> MessageTypeInfo =
		(JsonTypeInfo<JsonRpcMessage>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage));

	private readonly HttpListener _listener = new();
	private readonly byte[] _expectedAuthorization;
	private readonly McpServerOptions _serverOptions;
	private readonly ILoggerFactory? _loggerFactory;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _stopping = new();
	private readonly HashSet<Task> _requests = [];
	private Task? _acceptLoop;

	public McpHttpServer(int port, string accessToken, McpServerOptions serverOptions, ILoggerFactory? loggerFactory, ILogger logger)
	{
		// "localhost" rather than 127.0.0.1: http.sys lets a user without admin rights listen on it, and
		// requests whose Host header names anything else don't match the prefix, which stops DNS rebinding.
		_listener.Prefixes.Add($"http://localhost:{port}/");
		_expectedAuthorization = Encoding.UTF8.GetBytes($"Bearer {accessToken}");
		_serverOptions = serverOptions;
		_loggerFactory = loggerFactory;
		_logger = logger;
	}

	public void Start()
	{
		_listener.Start();
		_acceptLoop = AcceptLoopAsync();
	}

	private async Task AcceptLoopAsync()
	{
		while (!_stopping.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = await _listener.GetContextAsync();
			}
			catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
			{
				// The listener was stopped
				return;
			}

			var request = HandleAsync(context);
			lock (_requests)
			{
				_requests.Add(request);
			}

			_ = request.ContinueWith(
				t =>
				{
					lock (_requests)
					{
						_requests.Remove(t);
					}
				},
				TaskScheduler.Default);
		}
	}

	private async Task HandleAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;
		try
		{
			var status = Reject(request);
			if (status != null)
			{
				response.StatusCode = (int)status;
				if (status == HttpStatusCode.Unauthorized)
				{
					response.AddHeader("WWW-Authenticate", "Bearer");
				}

				return;
			}

			JsonRpcMessage? message;
			try
			{
				message = await JsonSerializer.DeserializeAsync(request.InputStream, MessageTypeInfo, _stopping.Token);
			}
			catch (JsonException)
			{
				message = null;
			}

			if (message == null)
			{
				response.StatusCode = (int)HttpStatusCode.BadRequest;
				return;
			}

			await ProcessAsync(message, response);
		}
		catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "MCP request failed");
			TrySetStatus(response, HttpStatusCode.InternalServerError);
		}
		finally
		{
			try
			{
				response.Close();
			}
			catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
			{
				// The client went away
			}
		}
	}

	private HttpStatusCode? Reject(HttpListenerRequest request)
	{
		if (!string.Equals(request.Url?.AbsolutePath, Path, StringComparison.Ordinal))
		{
			return HttpStatusCode.NotFound;
		}

		// A web page the user has open could otherwise reach the server through the browser
		var origin = request.Headers["Origin"];
		if (origin != null && !IsLocalOrigin(origin))
		{
			return HttpStatusCode.Forbidden;
		}

		if (!IsAuthorized(request.Headers["Authorization"]))
		{
			return HttpStatusCode.Unauthorized;
		}

		// Stateless mode has no stream for messages outside a response, so there is nothing to GET,
		// and no session to DELETE
		if (!string.Equals(request.HttpMethod, "POST", StringComparison.Ordinal))
		{
			return HttpStatusCode.MethodNotAllowed;
		}

		var accept = request.Headers["Accept"] ?? "";
		if (!accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
			|| !accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
		{
			return HttpStatusCode.NotAcceptable;
		}

		return null;
	}

	private bool IsAuthorized(string? authorization)
	{
		return authorization != null
			&& CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(authorization), _expectedAuthorization);
	}

	private static bool IsLocalOrigin(string origin)
	{
		return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
	}

	private async Task ProcessAsync(JsonRpcMessage message, HttpListenerResponse response)
	{
		response.StatusCode = (int)HttpStatusCode.OK;
		response.ContentType = "text/event-stream";
		response.AddHeader("Cache-Control", "no-cache, no-store");
		response.SendChunked = true;

		var transport = new StreamableHttpServerTransport(_loggerFactory) { Stateless = true };
		await using var server = McpServer.Create(transport, _serverOptions, _loggerFactory);
		var run = server.RunAsync(_stopping.Token);
		try
		{
			var body = new DeferredStartStream(response.OutputStream);
			var wroteResponse = await transport.HandlePostRequestAsync(message, body, _stopping.Token);
			if (!wroteResponse && !body.Started)
			{
				// A notification or a response from the client: there is nothing to answer with
				response.StatusCode = (int)HttpStatusCode.Accepted;
				response.ContentType = null;
				response.SendChunked = false;
				response.ContentLength64 = 0;
			}
		}
		finally
		{
			await transport.DisposeAsync();
			try
			{
				await run;
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	private static void TrySetStatus(HttpListenerResponse response, HttpStatusCode status)
	{
		try
		{
			response.StatusCode = (int)status;
		}
		catch (InvalidOperationException)
		{
			// Headers are already out
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();
		_listener.Close();

		if (_acceptLoop != null)
		{
			await _acceptLoop;
		}

		Task[] requests;
		lock (_requests)
		{
			requests = [.. _requests];
		}

		await Task.WhenAll(requests);
		_stopping.Dispose();
	}

	/// <summary>
	/// Holds back flushes until something is written, so the status can still change to 202 when the
	/// transport has nothing to send: <see cref="HttpListenerResponse"/> sends its headers on the first flush.
	/// </summary>
	private sealed class DeferredStartStream(Stream inner) : Stream
	{
		public bool Started { get; private set; }

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Flush()
		{
			if (Started)
			{
				inner.Flush();
			}
		}

		public override Task FlushAsync(CancellationToken cancellationToken)
		{
			return Started ? inner.FlushAsync(cancellationToken) : Task.CompletedTask;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			Started = true;
			inner.Write(buffer, offset, count);
		}

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			Started = true;
			return inner.WriteAsync(buffer, cancellationToken);
		}

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			Started = true;
			return inner.WriteAsync(buffer, offset, count, cancellationToken);
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();
	}
}
