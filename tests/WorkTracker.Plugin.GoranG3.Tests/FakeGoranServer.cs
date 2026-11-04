using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorkTracker.Plugin.GoranG3.Tests;

/// <summary>
/// Answers MCP over HTTP the way the Goran G3 server does, with the real MCP client talking to it.
/// Records every tool call together with the address it went to, for any number of addresses.
/// </summary>
internal sealed class FakeGoranServer
{
	public sealed record ToolCall(string Host, string Tool, JsonObject Arguments, string? Authorization);

	private readonly List<ToolCall> _toolCalls = [];

	public IReadOnlyList<ToolCall> ToolCalls => _toolCalls;

	/// <summary>Nothing answers: every request fails the way an unreachable server does.</summary>
	public bool IsDown { get; set; }

	/// <summary>Tickets the server refuses to create an item for, by the text the item carries.</summary>
	public HashSet<string> RejectedTexts { get; } = [];

	public bool RejectsSubmit { get; set; }

	public IEnumerable<ToolCall> CallsOf(string tool) => _toolCalls.Where(c => c.Tool == tool);

	public HttpMessageHandler CreateHandler() => new Handler(this);

	private HttpResponseMessage Respond(HttpRequestMessage request, string body)
	{
		var message = JsonNode.Parse(body)!.AsObject();
		var method = message["method"]!.GetValue<string>();

		if (method.StartsWith("notifications/", StringComparison.Ordinal))
		{
			return new HttpResponseMessage(HttpStatusCode.Accepted);
		}

		JsonNode? result = method switch
		{
			"initialize" => new JsonObject
			{
				["protocolVersion"] = message["params"]!["protocolVersion"]!.GetValue<string>(),
				["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
				["serverInfo"] = new JsonObject { ["name"] = "fake-goran", ["version"] = "1.0" }
			},
			"tools/list" => new JsonObject
			{
				["tools"] = new JsonArray(
					Tool("create_my_timesheet_item"),
					Tool("submit_my_timesheet"))
			},
			"tools/call" => CallTool(request, message),
			_ => null
		};

		var response = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"] = message["id"]!.DeepClone()
		};

		if (result != null)
		{
			response["result"] = result;
		}
		else
		{
			// The client asks for newer protocol features first, and falls back when told there are none
			response["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" };
		}

		return new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json")
		};
	}

	private static JsonObject Tool(string name) => new()
	{
		["name"] = name,
		["inputSchema"] = new JsonObject { ["type"] = "object" }
	};

	private JsonObject CallTool(HttpRequestMessage request, JsonObject message)
	{
		var tool = message["params"]!["name"]!.GetValue<string>();
		var arguments = message["params"]!["arguments"]?.AsObject() ?? [];
		_toolCalls.Add(new ToolCall(request.RequestUri!.Host, tool, arguments.DeepClone().AsObject(), request.Headers.Authorization?.Parameter));

		var text = arguments["text"]?.GetValue<string>();
		var rejected = tool switch
		{
			"submit_my_timesheet" => RejectsSubmit,
			_ => text != null && RejectedTexts.Contains(text)
		};

		return new JsonObject
		{
			["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = rejected ? "rejected" : "ok" }),
			["isError"] = rejected
		};
	}

	private sealed class Handler(FakeGoranServer server) : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (server.IsDown)
			{
				throw new HttpRequestException("No connection could be made");
			}

			// The stream for messages from the server, and ending a session: this server has neither
			if (request.Method != HttpMethod.Post)
			{
				return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
			}

			var body = await request.Content!.ReadAsStringAsync(cancellationToken);
			return server.Respond(request, body);
		}
	}
}
