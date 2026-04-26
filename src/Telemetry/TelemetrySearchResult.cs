namespace TelegramAIBot.Telemetry;

internal sealed record TelemetrySearchResult(
	DateTime CreatedUtc,
	string Summary,
	string Payload,
	float Score
);
