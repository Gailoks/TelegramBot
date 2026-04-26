namespace TelegramAIBot.Telemetry;

internal interface ITelemetryStorage
{
	public Task CreateEntryAsync(string user, TelemetryEntry entry);
	public Task<IReadOnlyList<TelemetrySearchResult>> SearchRelevantAsync(string user, string query, int limit = 5);
	public Task<string?> TryLoadLatestPayloadAsync(string user);
}
