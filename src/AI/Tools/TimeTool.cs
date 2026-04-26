using Newtonsoft.Json;

namespace TelegramAIBot.AI.Tools;

internal static class TimeTool
{
	public static ToolDefinition Create()
	{
		return new ToolDefinition(
			"get_time",
			"Get the current UTC time.",
			new
			{
				type = "object",
				properties = new { },
				required = Array.Empty<string>()
			},
			_ =>
			{
				var now = DateTimeOffset.UtcNow;
				return Task.FromResult(JsonConvert.SerializeObject(new
				{
					utc = now.ToString("O"),
					unix = now.ToUnixTimeSeconds()
				}, Formatting.Indented));
			}
		);
	}
}
