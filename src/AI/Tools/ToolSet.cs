using TelegramAIBot.Telemetry;

namespace TelegramAIBot.AI.Tools;

internal static class ToolSet
{
	public static IReadOnlyCollection<ToolDefinition> CreateForUser(ITelemetryStorage telemetry, string userId, Func<TimeSpan, Task> scheduleRevoke)
	{
		return
		[
			TelemetrySearchTool.Create(telemetry, userId),
			TimerTool.Create(scheduleRevoke),
			TimeTool.Create()
		];
	}
}
