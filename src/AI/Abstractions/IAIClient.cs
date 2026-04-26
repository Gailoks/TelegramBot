using TelegramAIBot.AI.Tools;

namespace TelegramAIBot.AI.Abstractions
{
	internal interface IAIClient
	{
		public IAIChat CreateChat();

		public IAIChat CreateChat(IReadOnlyCollection<ToolDefinition>? tools = null);

		public IImageDescriber? CreateImageDescriber();
	}
}
