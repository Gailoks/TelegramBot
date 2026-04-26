using Newtonsoft.Json;

namespace TelegramAIBot.AI.Tools;

internal static class TimeTool
{
	public static ToolDefinition Create()
	{
		return new ToolDefinition(
			"get_time",
			"Get the current time in current timezone.",
			new
			{
				type = "object",
				properties = new { },
				required = Array.Empty<string>()
			},
			_ =>
			{
				var now = DateTimeOffset.Now; // Fixed using local time now
				return Task.FromResult(JsonConvert.SerializeObject(new
				{
					utc = now.ToString("O"),
					unix = now.ToUnixTimeSeconds()
				}, Formatting.Indented));
			}
		);
	}
}
