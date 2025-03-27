namespace TelegramAIBot.Telemetry;

internal record class TelemetryEntry(IReadOnlyDictionary<string, object?> Data);
