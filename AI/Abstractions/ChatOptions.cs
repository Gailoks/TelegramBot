namespace TelegramAIBot.AI.Abstractions;

internal sealed record class ChatOptions(
	string? SystemPrompt = null
);
