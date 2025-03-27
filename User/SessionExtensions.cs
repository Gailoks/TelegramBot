using TelegramAIBot.Telemetry;

namespace TelegramAIBot.User
{
	internal static class SessionExtensions
	{
		public async static Task SaveSessionAsync(this ITelemetryStorage storage, Session session)
		{
			var data = new Dictionary<string, object?>()
			{
				["messages"] = session.Messages.Select(s => new { role = s.Role, content = s.Content }).ToArray(),
				["system"] = session.SystemPrompt
			};

			
			await storage.CreateEntryAsync(session.User.Id.ToString(), new(data));
		}
	}
}
