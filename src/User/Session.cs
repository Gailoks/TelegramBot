using TelegramAIBot.AI.Abstractions;

namespace TelegramAIBot.User;

class Session(IAIChat chat, UserRepositoryAccessor user)
{
    private readonly IAIChat _chat = chat;


	public IReadOnlyList<Message> Messages => (IReadOnlyList<Message>)_chat.Messages;

	public UserRepositoryAccessor User { get; } = user;
	

	public async Task<string> AskAsync(string question)
    {
        _chat.Messages.Add(new(MessageRole.User, question));
        return (await _chat.CreateChatCompletionAsync()).Content;
    }

	public string? SystemPrompt
	{
		get => _chat.Options.SystemPrompt;
		set => _chat.ModifyOptions(options => options with { SystemPrompt = value });
	}


	public string? ContextSummary
	{
		get => _chat.Options.ContextSummary;
		set => _chat.ModifyOptions(options => options with { ContextSummary = value });
	}
}
