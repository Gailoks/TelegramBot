using TelegramAIBot.AI.Abstractions;

namespace TelegramAIBot.AI.Gug
{
	internal sealed class GugClient : IAIClient
	{
		public static readonly EventId ChatCreatedLOG = new EventId(11, nameof(ChatCreatedLOG)).Form();


		private readonly Configuration _configuration;
		private readonly ILogger<GugClient>? _logger;


		public GugClient(IOptions<Configuration> configuration, ILogger<GugClient>? logger = null)
		{
			_configuration = configuration.Value;
			_logger = logger;
		}


		public IAIChat CreateChat()
		{
			var chat = new GugChat(_logger)
			{
				ChatCompletionCreationOperationDuration = _configuration.ChatCompletionCreationOperationDuration
			};

			_logger?.Log(LogLevel.Information, ChatCreatedLOG, "New gug chat with id {Id} created", chat.Id);

			return chat;
		}


		public class Configuration
		{
			public TimeSpan? ChatCompletionCreationOperationDuration { get; init; } = null;
		}
	}
}
