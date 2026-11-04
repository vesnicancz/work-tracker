using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Identity.Client.Extensibility;

namespace WorkTracker.Infrastructure.Auth;

/// <summary>
/// Runs the authorization code step in the system browser and catches the redirect on our own loopback listener.
/// MSAL's built-in system browser flow never returns from <c>ExecuteAsync</c> under Avalonia; with a custom web UI
/// MSAL only awaits this task, then redeems the code (PKCE included) and caches the tokens itself.
/// </summary>
internal sealed class SystemBrowserWebUi(Action<Uri> openBrowser, TimeSpan timeout) : ICustomWebUi
{
	private const string SuccessPage =
		"<!doctype html><html><head><meta charset=\"utf-8\"><title>WorkTracker</title></head>" +
		"<body style=\"font-family:sans-serif;text-align:center;margin-top:4em\">" +
		"<h2>Sign-in complete</h2><p>You can close this window and return to WorkTracker.</p></body></html>";

	private const string FailurePage =
		"<!doctype html><html><head><meta charset=\"utf-8\"><title>WorkTracker</title></head>" +
		"<body style=\"font-family:sans-serif;text-align:center;margin-top:4em\">" +
		"<h2>Sign-in failed</h2><p>Return to WorkTracker for details.</p></body></html>";

	public static int GetFreeLoopbackPort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			return ((IPEndPoint)listener.LocalEndpoint).Port;
		}
		finally
		{
			listener.Stop();
		}
	}

	public async Task<Uri> AcquireAuthorizationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken)
	{
		using var listener = new HttpListener();
		listener.Prefixes.Add(redirectUri.GetLeftPart(UriPartial.Authority) + "/");
		listener.Start();

		openBrowser(authorizationUri);

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);

		while (true)
		{
			HttpListenerContext context;
			try
			{
				context = await listener.GetContextAsync().WaitAsync(timeoutCts.Token);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				throw new TimeoutException($"Sign-in was not completed in the browser within {timeout.TotalMinutes:0} minutes.");
			}

			var query = context.Request.QueryString;
			if (query["code"] == null && query["error"] == null)
			{
				// Favicon and other stray requests the browser sends to the loopback port
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				context.Response.Close();
				continue;
			}

			await WritePageAsync(context.Response, query["error"] == null ? SuccessPage : FailurePage, cancellationToken);
			return context.Request.Url!;
		}
	}

	private static async Task WritePageAsync(HttpListenerResponse response, string html, CancellationToken cancellationToken)
	{
		var body = Encoding.UTF8.GetBytes(html);
		response.ContentType = "text/html; charset=utf-8";
		response.ContentLength64 = body.Length;
		await response.OutputStream.WriteAsync(body, cancellationToken);
		response.Close();
	}
}
