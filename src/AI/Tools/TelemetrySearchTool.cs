using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TelegramAIBot.Telemetry;

namespace TelegramAIBot.AI.Tools;

internal static class TelemetrySearchTool
{
	public static ToolDefinition Create(ITelemetryStorage telemetry, string userId)
	{
		return new ToolDefinition(
			"search",
			"Memory search attempt. Search relevant stored telemetry entries for a query and return the closest matches.",
			new
			{
				type = "object",
				properties = new
				{
					query = new
					{
						type = "string",
						description = "Search query text."
					},
					limit = new
					{
						type = "integer",
						description = "Maximum number of entries to return.",
						minimum = 1,
						maximum = 10
					}
				},
				required = new[] { "query" }
			},
			async arguments =>
			{
				var payload = JObject.Parse(arguments);
				var query = payload["query"]?.Value<string>() ?? string.Empty;
				var limit = payload["limit"]?.Value<int?>() ?? 5;
				var results = await telemetry.SearchRelevantAsync(userId, query, limit);
				return JsonConvert.SerializeObject(results, Formatting.Indented);
			}
		);
	}
}
