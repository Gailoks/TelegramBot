using Newtonsoft.Json;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace TelegramAIBot.Telemetry;

internal sealed class FileBasedTelemetryStorage : ITelemetryStorage
{
	private readonly Options _options;


	public FileBasedTelemetryStorage(IOptions<Options> options)
	{
		_options = options.Value;
	}


	public async Task CreateEntryAsync(string user, TelemetryEntry entry)
	{
		if (Directory.Exists(_options.Path) == false)
			Directory.CreateDirectory(_options.Path);

		var archivePath = Path.Combine(_options.Path, user + ".zip");
		using var file = File.Open(archivePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
		using var archive = new ZipArchive(file, ZipArchiveMode.Update, leaveOpen: true);
		
		var now = DateTime.UtcNow;
		var entryName = now.ToString("dd_MM_yyyy") + ".telemetry";

		var archiveEntry = archive.GetEntry(entryName);

		archiveEntry ??= archive.CreateEntry(entryName, CompressionLevel.SmallestSize);

		using var entryStream = archiveEntry.Open();
		entryStream.Seek(entryStream.Length, SeekOrigin.Begin);
		using var writer = new StreamWriter(entryStream, Encoding.UTF8);

		await WriteEntryAsync(entry, now, writer);

		await writer.FlushAsync();
	}

	private static async Task WriteEntryAsync(TelemetryEntry entry, DateTime now, StreamWriter destination)
	{
		await destination.WriteAsync(now.ToString("hh:mm:ss MM/dd/yy: ", CultureInfo.InvariantCulture));
		var data = JsonConvert.SerializeObject(entry.Data, new JsonSerializerSettings() { Formatting = Formatting.None, Converters = [new Newtonsoft.Json.Converters.StringEnumConverter()] });
		await destination.WriteLineAsync(data);
	}


	public class Options
	{
		public required string Path { get; init; }
	}
}
