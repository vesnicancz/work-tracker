using System.Globalization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Mcp;

/// <summary>
/// Lets AI assistants drive WorkTracker over MCP. The server listens on localhost only while this
/// plugin is enabled and the GUI is running, and wants the access token from the configuration.
/// </summary>
public sealed class McpPlugin : RemoteControlPluginBase
{
	private const int DefaultPort = 47_821;

	private static class ConfigKeys
	{
		public const string Port = "port";
		public const string AccessToken = "access_token";
	}

	private readonly ILoggerFactory? _loggerFactory;
	private readonly SemaphoreSlim _serverLock = new(1, 1);
	private McpHttpServer? _server;
	private int _port = DefaultPort;
	private string _accessToken = "";

	public McpPlugin(ILogger<McpPlugin> logger)
		: this(logger, null)
	{
	}

	internal McpPlugin(ILogger<McpPlugin> logger, ILoggerFactory? loggerFactory)
		: base(logger)
	{
		_loggerFactory = loggerFactory;
	}

	public override PluginMetadata Metadata => new()
	{
		Id = "mcp.server",
		Name = "MCP Server",
		Version = new Version(0, 1, 0),
		Author = "WorkTracker Team",
		Description = "Lets AI assistants such as Claude Code read and control work tracking over the Model Context Protocol.",
		Tags = ["mcp", "ai", "remote-control"]
	};

	public int Port => _port;

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return
		[
			new PluginConfigurationField
			{
				Key = ConfigKeys.Port,
				Label = "Port",
				Description = "The localhost port the MCP server listens on, at http://localhost:<port>/mcp.",
				Type = PluginConfigurationFieldType.Number,
				DefaultValue = DefaultPort.ToString(CultureInfo.InvariantCulture),
				Placeholder = DefaultPort.ToString(CultureInfo.InvariantCulture),
				ValidationPattern = "^(?:102[4-9]|10[3-9][0-9]|1[1-9][0-9]{2}|[2-9][0-9]{3}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|655[0-2][0-9]|6553[0-5])$",
				ValidationMessage = "Must be a port from 1024 to 65535"
			},
			new PluginConfigurationField
			{
				Key = ConfigKeys.AccessToken,
				Label = "Access token",
				Description = "The MCP client sends it as \"Authorization: Bearer <token>\". Use a long random value.",
				Type = PluginConfigurationFieldType.Password,
				IsRequired = true,
				ValidationPattern = "^\\S{16,}$",
				ValidationMessage = "Must be at least 16 characters without spaces"
			}
		];
	}

	protected override Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		_port = int.TryParse(GetConfigValue(ConfigKeys.Port), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
			? port
			: DefaultPort;
		_accessToken = GetConfigValue(ConfigKeys.AccessToken) ?? "";

		return Task.FromResult(_accessToken.Length > 0);
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

			var options = new McpServerOptions
			{
				ServerInfo = new Implementation { Name = "WorkTracker", Version = Metadata.Version.ToString() },
				ToolCollection = [.. WorkTrackerTools.Create(host)]
			};

			var server = new McpHttpServer(_port, _accessToken, options, _loggerFactory, Logger);
			server.Start();
			_server = server;
			Logger.LogInformation("MCP server listening on http://localhost:{Port}{Path}", _port, McpHttpServer.Path);
		}
		finally
		{
			_serverLock.Release();
		}
	}

	private async Task DetachAsync()
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
