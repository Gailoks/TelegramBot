using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TelegramAIBot.AI.Tools;

internal static class TimerTool
{
	private static readonly Regex DurationPattern = new("^(?<value>\\d+)(?<unit>[mMhH])$", RegexOptions.Compiled);


	public static ToolDefinition Create(Func<TimeSpan, Task> scheduleRevoke)
	{
		return new ToolDefinition(
			"timer",
			"Schedule session revoke after a period of time.",
			new
			{
				type = "object",
				properties = new
				{
					time = new
					{
						type = "string",
						description = "Simple duration format like 1m or 1h. Maximum allowed value is 4h."
					}
				},
				required = new[] { "time" }
			},
			async arguments =>
			{
				var payload = JObject.Parse(arguments);
				var rawTime = payload["time"]?.Value<string>()?.Trim() ?? string.Empty;
				var duration = ParseDuration(rawTime);

				if (duration > TimeSpan.FromHours(4))
					throw new ArgumentOutOfRangeException(nameof(arguments), "Timer cannot exceed 4 hours.");

				await scheduleRevoke(duration);

				return JsonConvert.SerializeObject(new
				{
					ok = true,
					time = rawTime,
					expires_at_utc = DateTimeOffset.UtcNow.Add(duration).ToString("O"),
					waited_seconds = (int)duration.TotalSeconds
				}, Formatting.Indented);
			}
		);
	}


	private static TimeSpan ParseDuration(string value)
	{
		var match = DurationPattern.Match(value);
		if (match.Success == false)
			throw new FormatException("Duration must be in simple format like 1m or 1h.");

		var amount = int.Parse(match.Groups["value"].Value);
		var unit = char.ToLowerInvariant(match.Groups["unit"].Value[0]);

		return unit switch
		{
			'm' => TimeSpan.FromMinutes(amount),
			'h' => TimeSpan.FromHours(amount),
			_ => throw new FormatException("Unsupported duration unit.")
		};
	}
}
