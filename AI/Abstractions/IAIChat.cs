namespace TelegramAIBot.AI.Abstractions;

internal interface IAIChat
{
	public ChatOptions Options { get; set; }

	public IList<Message> Messages { get; } 

	public Guid Id { get; }


	public void ModifyOptions(Func<ChatOptions, ChatOptions> modification);

	public Task<Message> CreateChatCompletionAsync();
}
