using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace WorkTracker.Avalonia.Services.Linux;

/// <summary>A shortcut to register with the desktop, as described to the user in its settings UI.</summary>
/// <param name="Id">Stable id the portal reports back in the Activated signal.</param>
/// <param name="Description">User-readable text shown in the desktop's shortcut settings.</param>
/// <param name="PreferredTrigger">
/// Suggested key combination in the freedesktop.org shortcuts syntax (<c>CTRL+SHIFT+w</c>): modifiers
/// out of CTRL/ALT/SHIFT/NUM/LOGO, then a key named after its xkbcommon keysym without the
/// <c>XKB_KEY_</c> prefix. Only a suggestion — the desktop may bind something else, and the user can
/// rebind it afterwards.
/// </param>
internal sealed record PortalShortcut(string Id, string Description, string? PreferredTrigger);

/// <summary>
/// Registers global shortcuts through the <c>org.freedesktop.portal.GlobalShortcuts</c> desktop
/// portal, which is the only way to get them under Wayland: a Wayland client cannot see input
/// aimed at another window, so the compositor has to own the binding and tell us when it fires.
/// <para>
/// The portal hands the shortcut to the desktop's own settings, so the user can see it, rebind it,
/// or take it away. Availability varies — KDE Plasma and GNOME 46+ implement it, wlroots compositors
/// need xdg-desktop-portal-wlr or -hyprland — so every failure here is reported, never thrown: the
/// app works without the shortcut.
/// </para>
/// </summary>
internal sealed class GlobalShortcutsPortal : IAsyncDisposable
{
	private const string PortalService = "org.freedesktop.portal.Desktop";
	private const string ShortcutsInterface = "org.freedesktop.portal.GlobalShortcuts";
	private const string RequestInterface = "org.freedesktop.portal.Request";
	private const string SessionInterface = "org.freedesktop.portal.Session";
	private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
	private const string RegistryInterface = "org.freedesktop.host.portal.Registry";
	private const string RegistryPath = "/org/freedesktop/host/portal/registry";

	/// <summary>Plain request/reply calls; a portal that does not answer this fast is not there.</summary>
	private static readonly TimeSpan s_callTimeout = TimeSpan.FromSeconds(30);

	/// <summary>
	/// BindShortcuts may put a confirmation dialog in front of the user (GNOME does), so it gets long
	/// enough for someone to notice and answer it, not a machine-speed timeout.
	/// </summary>
	private static readonly TimeSpan s_bindTimeout = TimeSpan.FromMinutes(5);

	private readonly ILogger _logger;
	private readonly List<IDisposable> _subscriptions = [];
	private readonly SemaphoreSlim _gate = new(1, 1);

	private DBusConnection? _connection;
	private string? _sessionHandle;
	private bool _disposed;

	public GlobalShortcutsPortal(ILogger logger)
	{
		_logger = logger;
	}

	/// <summary>Raised on a D-Bus worker thread with the id of the shortcut the user pressed.</summary>
	public event Action<string>? Activated;

	/// <summary>
	/// Opens a portal session and binds <paramref name="shortcuts"/>, returning whether the desktop
	/// took them. Safe to call when no portal, no session bus or no compositor support is present —
	/// it logs why and returns false.
	/// </summary>
	public async Task<bool> TryBindAsync(IReadOnlyList<PortalShortcut> shortcuts, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(shortcuts);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_disposed || _sessionHandle != null)
			{
				return _sessionHandle != null;
			}

			return await BindCoreAsync(shortcuts, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Global shortcuts portal unavailable; the global hotkey will not work");
			await ResetAsync().ConfigureAwait(false);
			return false;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<bool> BindCoreAsync(IReadOnlyList<PortalShortcut> shortcuts, CancellationToken cancellationToken)
	{
		var address = DBusAddress.Session;
		if (string.IsNullOrEmpty(address))
		{
			_logger.LogInformation("No D-Bus session bus; global shortcuts are unavailable");
			return false;
		}

		var connection = new DBusConnection(address);
		_connection = connection;
		await connection.ConnectAsync().ConfigureAwait(false);

		await TryRegisterAppIdAsync(connection).ConfigureAwait(false);

		var version = await TryGetPortalVersionAsync(connection).ConfigureAwait(false);
		if (version == null)
		{
			_logger.LogInformation(
				"The desktop portal does not implement {Interface}; global shortcuts are unavailable",
				ShortcutsInterface);
			await ResetAsync().ConfigureAwait(false);
			return false;
		}

		_logger.LogDebug("Global shortcuts portal version {Version}", version);

		var sessionHandle = await CreateSessionAsync(connection, cancellationToken).ConfigureAwait(false);
		if (sessionHandle == null)
		{
			await ResetAsync().ConfigureAwait(false);
			return false;
		}

		// Subscribe and record the session before binding: on a desktop that binds without asking,
		// the user could press the combination the moment BindShortcuts returns, and a bind that
		// fails still leaves a session for ResetAsync to close.
		await SubscribeToSessionAsync(connection, sessionHandle).ConfigureAwait(false);
		_sessionHandle = sessionHandle;

		if (!await BindShortcutsAsync(connection, sessionHandle, shortcuts, cancellationToken).ConfigureAwait(false))
		{
			await ResetAsync().ConfigureAwait(false);
			return false;
		}

		return true;
	}

	/// <summary>
	/// Tells the portal which application this is, before any other call on this connection — the
	/// order the host registry demands, and the reason this connection is ours alone rather than
	/// one shared with Avalonia's own portal traffic.
	/// <para>
	/// It matters because the desktop files the shortcut under the app id: with one, the binding
	/// lands under <see cref="DesktopEntry.AppId"/>, shows the name and icon from our installed
	/// desktop entry, and survives restarts and rebinding by the user; without one, the portal
	/// invents a per-session name and the shortcut is forgotten the moment the app exits.
	/// </para>
	/// <para>
	/// The portal can also work the app id out on its own when the session launched us from the
	/// desktop entry into a systemd scope, which is what happens on a normal Plasma or GNOME login;
	/// this call is what covers the other ways of starting the app. It needs xdg-desktop-portal 1.21
	/// or newer and is simply absent before that, so a failure here is logged and ignored.
	/// </para>
	/// </summary>
	private async Task TryRegisterAppIdAsync(DBusConnection connection)
	{
		try
		{
			MessageBuffer message;
			var writer = connection.GetMessageWriter();
			try
			{
				writer.WriteMethodCallHeader(
					destination: PortalService,
					path: RegistryPath,
					@interface: RegistryInterface,
					member: "Register",
					signature: "sa{sv}",
					flags: MessageFlags.None);
				writer.WriteString(DesktopEntry.AppId);
				var options = writer.WriteDictionaryStart();
				writer.WriteDictionaryEnd(options);
				message = writer.CreateMessage();
			}
			finally
			{
				writer.Dispose();
			}

			await connection.CallMethodAsync(message).WaitAsync(s_callTimeout).ConfigureAwait(false);
			_logger.LogDebug("Registered with the portal as {AppId}", DesktopEntry.AppId);
		}
		catch (Exception ex)
		{
			// Older portals have no registry at all, and one that already knows who we are rejects
			// the call. Either way the shortcut still binds.
			_logger.LogDebug(ex, "Could not announce the app id to the portal");
		}
	}

	/// <summary>
	/// Reads the interface's version property, which doubles as the availability probe: a portal
	/// without a GlobalShortcuts backend answers with an error rather than a version.
	/// </summary>
	private async Task<uint?> TryGetPortalVersionAsync(DBusConnection connection)
	{
		try
		{
			MessageBuffer message;
			var writer = connection.GetMessageWriter();
			try
			{
				writer.WriteMethodCallHeader(
					destination: PortalService,
					path: PortalHandles.ObjectPath,
					@interface: PropertiesInterface,
					member: "Get",
					signature: "ss",
					flags: MessageFlags.None);
				writer.WriteString(ShortcutsInterface);
				writer.WriteString("version");
				message = writer.CreateMessage();
			}
			finally
			{
				writer.Dispose();
			}

			return await connection
				.CallMethodAsync(message, static (m, _) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null)
				.WaitAsync(s_callTimeout)
				.ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is DBusErrorReplyException or TimeoutException)
		{
			_logger.LogDebug(ex, "Probing the global shortcuts portal failed");
			return null;
		}
	}

	private async Task<string?> CreateSessionAsync(DBusConnection connection, CancellationToken cancellationToken)
	{
		var handleToken = PortalHandles.NewToken();
		var sessionToken = PortalHandles.NewToken();

		MessageBuffer message;
		var writer = connection.GetMessageWriter();
		try
		{
			writer.WriteMethodCallHeader(
				destination: PortalService,
				path: PortalHandles.ObjectPath,
				@interface: ShortcutsInterface,
				member: "CreateSession",
				signature: "a{sv}",
				flags: MessageFlags.None);

			// Both tokens are documented as optional but the portal rejects the call without them.
			var options = writer.WriteDictionaryStart();
			WriteStringOption(ref writer, "handle_token", handleToken);
			WriteStringOption(ref writer, "session_handle_token", sessionToken);
			writer.WriteDictionaryEnd(options);

			message = writer.CreateMessage();
		}
		finally
		{
			writer.Dispose();
		}

		var response = await CallWithResponseAsync(connection, message, handleToken, s_callTimeout, cancellationToken)
			.ConfigureAwait(false);

		if (response.Code != PortalResponse.Success)
		{
			_logger.LogInformation("The portal declined the global shortcuts session (response {Code})", response.Code);
			return null;
		}

		if (!response.Results.TryGetValue("session_handle", out var handle))
		{
			_logger.LogWarning("The portal returned no session_handle for the global shortcuts session");
			return null;
		}

		// session_handle is an object path that the portal has always sent as a plain string; it
		// stays that way for compatibility, so accept both spellings.
		return handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();
	}

	private async Task<bool> BindShortcutsAsync(
		DBusConnection connection,
		string sessionHandle,
		IReadOnlyList<PortalShortcut> shortcuts,
		CancellationToken cancellationToken)
	{
		var handleToken = PortalHandles.NewToken();

		MessageBuffer message;
		var writer = connection.GetMessageWriter();
		try
		{
			writer.WriteMethodCallHeader(
				destination: PortalService,
				path: PortalHandles.ObjectPath,
				@interface: ShortcutsInterface,
				member: "BindShortcuts",
				signature: "oa(sa{sv})sa{sv}",
				flags: MessageFlags.None);

			writer.WriteObjectPath(sessionHandle);

			var array = writer.WriteArrayStart(DBusType.Struct);
			foreach (var shortcut in shortcuts)
			{
				writer.WriteStructureStart();
				writer.WriteString(shortcut.Id);

				var properties = writer.WriteDictionaryStart();
				WriteStringOption(ref writer, "description", shortcut.Description);
				if (!string.IsNullOrEmpty(shortcut.PreferredTrigger))
				{
					WriteStringOption(ref writer, "preferred_trigger", shortcut.PreferredTrigger);
				}
				writer.WriteDictionaryEnd(properties);
			}
			writer.WriteArrayEnd(array);

			// parent_window: an exported surface handle would parent the desktop's confirmation
			// dialog to our window. Avalonia does not expose one, so the dialog stands alone.
			writer.WriteString(string.Empty);

			var options = writer.WriteDictionaryStart();
			WriteStringOption(ref writer, "handle_token", handleToken);
			writer.WriteDictionaryEnd(options);

			message = writer.CreateMessage();
		}
		finally
		{
			writer.Dispose();
		}

		var response = await CallWithResponseAsync(connection, message, handleToken, s_bindTimeout, cancellationToken)
			.ConfigureAwait(false);

		if (response.Code == PortalResponse.Success)
		{
			return true;
		}

		_logger.LogInformation(
			response.Code == PortalResponse.Cancelled
				? "The user declined the global shortcut"
				: "The desktop did not bind the global shortcut (response {Code})",
			response.Code);
		return false;
	}

	/// <summary>
	/// Sends a portal call and waits for the <c>Response</c> signal that carries its real result.
	/// The match rule goes in before the call is sent, because the portal is free to answer the
	/// instant it has the message — after that, the signal would arrive with nobody listening and
	/// the wait would never end.
	/// </summary>
	private async Task<PortalResponse> CallWithResponseAsync(
		DBusConnection connection,
		MessageBuffer message,
		string handleToken,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
		var expectedPath = PortalHandles.RequestPath(connection.UniqueName!, handleToken);

		List<IDisposable> subscriptions = [await WatchResponseAsync(connection, expectedPath, completion).ConfigureAwait(false)];
		try
		{
			var actualPath = await connection
				.CallMethodAsync(message, static (m, _) => m.GetBodyReader().ReadObjectPathAsString(), null)
				.WaitAsync(timeout, cancellationToken)
				.ConfigureAwait(false);

			if (actualPath != expectedPath)
			{
				// Portals have honoured handle_token since 0.9; an older one picks its own path, so
				// pick that one up too rather than waiting for a signal that will never come.
				_logger.LogDebug("Portal answered on {Actual} instead of {Expected}", actualPath, expectedPath);
				subscriptions.Add(await WatchResponseAsync(connection, actualPath, completion).ConfigureAwait(false));
			}

			return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			// The rule has done its job the moment the Response arrives — and on failure it is the
			// only thing keeping a dead match on the bus.
			foreach (var subscription in subscriptions)
			{
				subscription.Dispose();
			}
		}
	}

	private static async ValueTask<IDisposable> WatchResponseAsync(
		DBusConnection connection,
		string requestPath,
		TaskCompletionSource<PortalResponse> completion)
	{
		return await connection.AddMatchAsync(
			new MatchRule
			{
				Type = MessageType.Signal,
				Sender = PortalService,
				Path = requestPath,
				Interface = RequestInterface,
				Member = "Response",
			},
			static (Message message, object? _) =>
			{
				var reader = message.GetBodyReader();
				var code = reader.ReadUInt32();
				return new PortalResponse(code, reader.ReadDictionaryOfStringToVariantValue());
			},
			static (Exception? exception, PortalResponse response, object? _, object? state) =>
			{
				var target = (TaskCompletionSource<PortalResponse>)state!;
				if (exception != null)
				{
					target.TrySetException(exception);
				}
				else
				{
					target.TrySetResult(response);
				}
			},
			null,
			completion,
			false,
			ObserverFlags.None).ConfigureAwait(false);
	}

	private async Task SubscribeToSessionAsync(DBusConnection connection, string sessionHandle)
	{
		_subscriptions.Add(await connection.AddMatchAsync(
			new MatchRule
			{
				Type = MessageType.Signal,
				Sender = PortalService,
				Path = PortalHandles.ObjectPath,
				Interface = ShortcutsInterface,
				Member = "Activated",
			},
			static (Message message, object? _) => ReadActivation(message),
			static (Exception? exception, (string Session, string Shortcut) activation, object? _, object? state) =>
			{
				if (exception == null)
				{
					((GlobalShortcutsPortal)state!).OnActivated(activation.Session, activation.Shortcut);
				}
			},
			null,
			this,
			false,
			ObserverFlags.None).ConfigureAwait(false));

		// The compositor can drop the session on its own (portal restart, user revoking the
		// shortcut), and then nothing would ever fire again — notice it instead of looking bound.
		_subscriptions.Add(await connection.AddMatchAsync(
			new MatchRule
			{
				Type = MessageType.Signal,
				Sender = PortalService,
				Path = sessionHandle,
				Interface = SessionInterface,
				Member = "Closed",
			},
			static (Message _, object? _) => true,
			static (Exception? exception, bool _, object? _, object? state) =>
			{
				if (exception == null)
				{
					((GlobalShortcutsPortal)state!).OnSessionClosed();
				}
			},
			null,
			this,
			false,
			ObserverFlags.None).ConfigureAwait(false));
	}

	/// <summary>
	/// Reads <c>Activated(o session_handle, s shortcut_id, t timestamp, a{sv} options)</c>. The
	/// session handle is declared as an object path but some portals send a string, and the two are
	/// indistinguishable once marshalled, so the message's own signature decides how to read it.
	/// </summary>
	private static (string Session, string Shortcut) ReadActivation(Message message)
	{
		var reader = message.GetBodyReader();
		var signature = message.SignatureAsString;
		var session = signature is ['s', ..] ? reader.ReadString() : reader.ReadObjectPathAsString();
		return (session, reader.ReadString());
	}

	private void OnActivated(string sessionHandle, string shortcutId)
	{
		if (!string.Equals(sessionHandle, _sessionHandle, StringComparison.Ordinal))
		{
			return;
		}

		try
		{
			Activated?.Invoke(shortcutId);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Handling global shortcut {Shortcut} failed", shortcutId);
		}
	}

	private void OnSessionClosed()
	{
		_logger.LogInformation("The desktop closed the global shortcuts session; the hotkey is no longer bound");
		_sessionHandle = null;
	}

	/// <summary>Writes one <c>{string, variant}</c> entry of an open a{sv}.</summary>
	private static void WriteStringOption(ref MessageWriter writer, string key, string value)
	{
		writer.WriteDictionaryEntryStart();
		writer.WriteString(key);
		writer.WriteVariantString(value);
	}

	/// <summary>
	/// Disposing twice is a no-op, as the contract requires. The gate itself is deliberately not
	/// disposed: a SemaphoreSlim only needs that when its wait handle has been used, and disposing
	/// it here would turn a second call into an ObjectDisposedException.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			await ResetAsync().ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>
	/// Closes the portal session and tears the connection down. Closing the session is what makes
	/// the shortcut disappear from the desktop's settings; dropping the connection alone would leave
	/// the portal to clean up on its own schedule.
	/// </summary>
	private async Task ResetAsync()
	{
		var connection = _connection;
		var sessionHandle = _sessionHandle;

		_sessionHandle = null;
		_connection = null;

		if (connection != null && sessionHandle != null)
		{
			try
			{
				MessageBuffer message;
				var writer = connection.GetMessageWriter();
				try
				{
					writer.WriteMethodCallHeader(
						destination: PortalService,
						path: sessionHandle,
						@interface: SessionInterface,
						member: "Close",
						signature: null,
						flags: MessageFlags.None);
					message = writer.CreateMessage();
				}
				finally
				{
					writer.Dispose();
				}

				await connection.CallMethodAsync(message).WaitAsync(s_callTimeout).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Closing the global shortcuts session failed");
			}
		}

		foreach (var subscription in _subscriptions)
		{
			subscription.Dispose();
		}
		_subscriptions.Clear();

		connection?.Dispose();
	}

	/// <summary>The <c>Response</c> signal of a portal request: an outcome code and its results.</summary>
	private sealed record PortalResponse(uint Code, Dictionary<string, VariantValue> Results)
	{
		internal const uint Success = 0;
		internal const uint Cancelled = 1;
	}
}
