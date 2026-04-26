namespace TelegramAIBot.AI.Tools;

internal sealed record ToolDefinition(
	string Name,
	string Description,
	object Parameters,
	Func<string, Task<string>> Handler
);
