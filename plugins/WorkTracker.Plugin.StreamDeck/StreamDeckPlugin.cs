using System.Globalization;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.StreamDeck;

/// <summary>
/// The WorkTracker side of the Stream Deck integration. The other side is the Stream Deck plugin in
/// <c>streamdeck-plugin/</c>, which runs inside the Stream Deck app and talks to this one over a
/// named pipe. The pipe exists only while this plugin is enabled and the GUI is running.
/// </summary>
public sealed class StreamDeckPlugin : RemoteControlPluginBase
{
	private const int DefaultResumeWindowDays = 7;

	private static class ConfigKeys
	{
		public const string ResumeWindowDays = "resume_window_days";
	}

	private readonly string _pipeName;
	private readonly SemaphoreSlim _serverLock = new(1, 1);
	private RemoteControlPipeServer? _server;
	private volatile int _resumeWindowDays = DefaultResumeWindowDays;

	public StreamDeckPlugin(ILogger<StreamDeckPlugin> logger)
		: this(logger, PipeEndpoint.Name)
	{
	}

	internal StreamDeckPlugin(ILogger<StreamDeckPlugin> logger, string pipeName)
		: base(logger)
	{
		_pipeName = pipeName;
	}

	public override PluginMetadata Metadata => new()
	{
		Id = "streamdeck.remote-control",
		Name = "Stream Deck",
		Version = new Version(1, 0, 0),
		Author = "WorkTracker Team",
		Description = "Control work tracking from an Elgato Stream Deck. Requires the WorkTracker plugin in the Stream Deck app.",
		Tags = ["streamdeck", "elgato", "remote-control"]
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return
		[
			new PluginConfigurationField
			{
				Key = ConfigKeys.ResumeWindowDays,
				Label = "Resume window (days)",
				Description = "How many days back a press on the dial can resume the most recent work. Older work is not offered.",
				Type = PluginConfigurationFieldType.Number,
				DefaultValue = DefaultResumeWindowDays.ToString(CultureInfo.InvariantCulture),
				Placeholder = DefaultResumeWindowDays.ToString(CultureInfo.InvariantCulture),
				ValidationPattern = "^[1-9][0-9]{0,2}$",
				ValidationMessage = "Must be a whole number of days from 1 to 999"
			}
		];
	}

	protected override async Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		_resumeWindowDays = int.TryParse(GetConfigValue(ConfigKeys.ResumeWindowDays), NumberStyles.None, CultureInfo.InvariantCulture, out var days)
			? days
			: DefaultResumeWindowDays;

		// The connected client may be showing work that no longer falls within the window, or the reverse
		await _serverLock.WaitAsync(cancellationToken);
		try
		{
			_server?.RefreshState();
		}
		finally
		{
			_serverLock.Release();
		}

		return true;
	}

	public override async Task AttachAsync(IRemoteControlHost host, CancellationToken cancellationToken)
	{
		await _serverLock.WaitAsync(cancellationToken);
		try
		{
			if (_server != null)
			{
				return;
			}

			_server = new RemoteControlPipeServer(_pipeName, host, () => TimeSpan.FromDays(_resumeWindowDays), Logger);
			_server.Start();
		}
		finally
		{
			_serverLock.Release();
		}
	}

	public override async Task DetachAsync()
	{
		await _serverLock.WaitAsync();
		try
		{
			if (_server != null)
			{
				await _server.DisposeAsync();
				_server = null;
			}
		}
		finally
		{
			_serverLock.Release();
		}
	}

	protected override async Task OnShutdownAsync()
	{
		await DetachAsync();
	}

	protected override async ValueTask OnDisposeAsync()
	{
		await DetachAsync();
	}
}
