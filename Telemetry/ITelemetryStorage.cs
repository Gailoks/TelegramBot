namespace TelegramAIBot.Telemetry;

internal interface ITelemetryStorage
{
	public Task CreateEntryAsync(string user, TelemetryEntry entry);
}
