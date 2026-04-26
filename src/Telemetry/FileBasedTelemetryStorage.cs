using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using TelegramAIBot.AI.OpenAI;
using TelegramAIBot.DataBase;

namespace TelegramAIBot.Telemetry;

internal sealed class FileBasedTelemetryStorage : ITelemetryStorage
{
	private readonly Options _options;
	private readonly UserContextDB _db;
	private readonly Lazy<Vectorizer?> _vectorizer;
	private readonly ILogger<FileBasedTelemetryStorage>? _logger;


	public FileBasedTelemetryStorage(IOptions<Options> options, UserContextDB db, IServiceProvider serviceProvider, ILogger<FileBasedTelemetryStorage>? logger = null)
	{
		_options = options.Value;
		_db = db;
		_logger = logger;
		_vectorizer = new(() => serviceProvider.GetService<OpenAIClient>()?.CreateVectorizer());

		_logger?.LogInformation("Telemetry storage configured. Path={Path}, VectorizerAvailable={VectorizerAvailable}", _options.Path, _vectorizer.Value is not null);
	}


	public async Task CreateEntryAsync(string user, TelemetryEntry entry)
	{
		_logger?.LogDebug("Telemetry entry create requested. User={User}", user);
		await ExecuteWithSchemaRetryAsync(async () =>
		{
			await EnsureSchemaAsync();

			var now = DateTime.UtcNow;
			var json = JsonConvert.SerializeObject(entry.Data, new JsonSerializerSettings
			{
				Formatting = Formatting.None,
				Converters = [new Newtonsoft.Json.Converters.StringEnumConverter()]
			});
			var summary = BuildSummary(entry);
			var compressedPayload = Compress(json);
			var embedding = await TryBuildEmbeddingAsync(summary);
			_logger?.LogDebug(
				"Telemetry entry prepared. User={User}, DataBytes={DataBytes}, SummaryChars={SummaryChars}, HasEmbedding={HasEmbedding}",
				user,
				Encoding.UTF8.GetByteCount(json),
				summary.Length,
				embedding is not null
			);

			_db.TelemetryRecords.Add(new TelemetryRecordDB
			{
				UserId = user,
				CreatedUtc = now,
				CompressedPayload = compressedPayload,
				Summary = summary,
				SummaryEmbeddingJson = embedding is null ? null : JsonConvert.SerializeObject(embedding)
			});

			await _db.SaveChangesAsync();
			_logger?.LogInformation("Telemetry entry saved. User={User}, SummaryChars={SummaryChars}", user, summary.Length);
			return true;
		});
	}


	public async Task<IReadOnlyList<TelemetrySearchResult>> SearchRelevantAsync(string user, string query, int limit = 5)
	{
		_logger?.LogDebug("Telemetry search requested. User={User}, QueryChars={QueryChars}, Limit={Limit}", user, query.Length, limit);
		return await ExecuteWithSchemaRetryAsync(async () =>
		{
			await EnsureSchemaAsync();

			var queryEmbedding = await GetQueryEmbeddingAsync(query);
			var records = await _db.TelemetryRecords
				.Where(record => record.UserId == user)
				.OrderByDescending(record => record.CreatedUtc)
				.ToListAsync();

			var results = records
				.Select(record =>
				{
					var recordEmbedding = DeserializeEmbedding(record.SummaryEmbeddingJson);
					var score = recordEmbedding is null
						? 0
						: CosineSimilarity(queryEmbedding, recordEmbedding);

					return new TelemetrySearchResult(
						record.CreatedUtc,
						record.Summary,
						Decompress(record.CompressedPayload),
						score
					);
				})
				.OrderByDescending(result => result.Score)
				.ThenByDescending(result => result.CreatedUtc)
				.Take(Math.Max(limit, 1))
				.ToArray();

			_logger?.LogInformation("Telemetry search completed. User={User}, ResultCount={ResultCount}", user, results.Length);
			return (IReadOnlyList<TelemetrySearchResult>)results;
		});
	}


	public async Task<string?> TryLoadLatestPayloadAsync(string user)
	{
		_logger?.LogDebug("Telemetry restore requested. User={User}", user);
		return await ExecuteWithSchemaRetryAsync(async () =>
		{
			await EnsureSchemaAsync();

			var record = await _db.TelemetryRecords
				.Where(record => record.UserId == user)
				.OrderByDescending(record => record.CreatedUtc)
				.FirstOrDefaultAsync();

			var payload = record is null ? null : Decompress(record.CompressedPayload);
			_logger?.LogInformation("Telemetry restore completed. User={User}, HasPayload={HasPayload}", user, payload is not null);
			return payload;
		});
	}


	private async Task<float[]> GetQueryEmbeddingAsync(string query)
	{
		var vectorizer = _vectorizer.Value;
		if (vectorizer is null)
			return [];

		return await vectorizer.Vectorize(query);
	}


	private async Task<float[]?> TryBuildEmbeddingAsync(string summary)
	{
		var vectorizer = _vectorizer.Value;
		if (vectorizer is null)
			return null;

		return await vectorizer.Vectorize(summary);
	}


	private async Task EnsureSchemaAsync()
	{
		_logger?.LogDebug("Ensuring telemetry schema.");
		await _db.EnsureSchemaAsync();
		_logger?.LogDebug("Telemetry schema ensured.");
	}


	private async Task<T> ExecuteWithSchemaRetryAsync<T>(Func<Task<T>> action)
	{
		try
		{
			return await action();
		}
		catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsMissingRelation(ex))
		{
			_logger?.LogWarning(ex, "Telemetry schema missing during DB update; retrying after schema bootstrap.");
			await _db.EnsureSchemaAsync();
			return await action();
		}
		catch (PostgresException ex) when (IsMissingRelation(ex))
		{
			_logger?.LogWarning(ex, "Telemetry schema missing during Postgres operation; retrying after schema bootstrap.");
			await _db.EnsureSchemaAsync();
			return await action();
		}
	}


	private async Task ExecuteWithSchemaRetryAsync(Func<Task> action)
	{
		await ExecuteWithSchemaRetryAsync(async () =>
		{
			await action();
			return true;
		});
	}


	private static bool IsMissingRelation(Exception ex)
	{
		return ex is PostgresException { SqlState: "42P01" }
			|| ex.InnerException is PostgresException { SqlState: "42P01" };
	}


	private static string BuildSummary(TelemetryEntry entry)
	{
		var root = JObject.FromObject(entry.Data);
		var system = root["system"]?.Value<string>();
		var contextSummary = root["contextSummary"]?.Value<string>();
		var messages = root["messages"] as JArray;
		var builder = new StringBuilder();

		if (string.IsNullOrWhiteSpace(system) == false)
			builder.AppendLine("System: " + system.Trim());

		if (string.IsNullOrWhiteSpace(contextSummary) == false)
		{
			builder.AppendLine("Summary: " + contextSummary.Trim());
			builder.AppendLine();
		}

		if (messages is not null)
		{
			foreach (var message in messages.TakeLast(16))
			{
				var role = message["role"]?.Value<string>() ?? "unknown";
				var content = message["content"]?.Value<string>() ?? "";
				if (content.Length > 600)
					content = content[..600] + "...";

				builder.AppendLine($"{role}: {content}".TrimEnd());
			}
		}

		var summary = builder.ToString().Trim();
		if (summary.Length > 4000)
			summary = summary[..4000];

		return summary;
	}


	private static byte[] Compress(string value)
	{
		var raw = Encoding.UTF8.GetBytes(value);
		using var output = new MemoryStream();
		using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
		{
			gzip.Write(raw, 0, raw.Length);
		}
		return output.ToArray();
	}


	private static string Decompress(byte[] value)
	{
		using var input = new MemoryStream(value);
		using var gzip = new GZipStream(input, CompressionMode.Decompress);
		using var reader = new StreamReader(gzip, Encoding.UTF8);
		return reader.ReadToEnd();
	}


	private static float[]? DeserializeEmbedding(string? embeddingJson)
	{
		return embeddingJson is null
			? null
			: JsonConvert.DeserializeObject<float[]>(embeddingJson);
	}


	private static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
	{
		if (left.Count != right.Count)
			return 0;

		double dot = 0;
		double leftMagnitude = 0;
		double rightMagnitude = 0;

		for (var i = 0; i < left.Count; i++)
		{
			var leftValue = left[i];
			var rightValue = right[i];

			dot += leftValue * rightValue;
			leftMagnitude += leftValue * leftValue;
			rightMagnitude += rightValue * rightValue;
		}

		if (leftMagnitude == 0 || rightMagnitude == 0)
			return 0;

		return (float)(dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude)));
	}


	public class Options
	{
		public string Path { get; init; } = string.Empty;
	}
}
