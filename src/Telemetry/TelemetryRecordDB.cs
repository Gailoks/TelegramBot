namespace TelegramAIBot.Telemetry;

internal sealed class TelemetryRecordDB
{
	public long Id { get; set; }

	public required string UserId { get; set; }

	public DateTime CreatedUtc { get; set; }

	public required byte[] CompressedPayload { get; set; }

	public required string Summary { get; set; }

	public string? SummaryEmbeddingJson { get; set; }
}
