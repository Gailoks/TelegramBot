using System.Collections.ObjectModel;
using TelegramAIBot.AI.Abstractions;

namespace TelegramAIBot.AI.Gug;

internal sealed class GugChat : IAIChat
{
	public static readonly EventId ChatCompletionCreatedLOG = new EventId(31, nameof(ChatCompletionCreatedLOG)).Form();
	public static readonly EventId ChatMessagesChangedLOG = new EventId(32, nameof(ChatMessagesChangedLOG)).Form();
	public static readonly EventId ChatOptionsChangedLOG = new EventId(33, nameof(ChatOptionsChangedLOG)).Form();


	private readonly ILogger<GugClient>? _logger;
	private readonly ObservableCollection<Message> _messages;
	private ChatOptions _options = new();


	public TimeSpan? ChatCompletionCreationOperationDuration { get; init; } = null;

	public ChatOptions Options { get => _options; set => _options = value; }

	public IList<Message> Messages => _messages;
	
	public Guid Id { get; } = Guid.NewGuid();


	public GugChat(ILogger<GugClient>? logger = null)
	{
		_logger = logger;
		_messages = new ObservableCollection<Message>();
		_messages.CollectionChanged += (s, e) =>
		{
			_logger?.Log(LogLevel.Trace, ChatMessagesChangedLOG, "Chat messages changes, action: {e}", e);
		};
	}

	public void ModifyOptions(Func<ChatOptions, ChatOptions> modification)
	{
		Options = modification(Options);
	}

	public async Task<Message> CreateChatCompletionAsync()
	{
		var messages = Messages;
		var lastMessageContent = messages[messages.Count - 1].Content;

		var result = new Message(MessageRole.Assistant,
			$"""
				Answer of Assistant to previous message
				Previous message:
				`{lastMessageContent}`

				Options: `{Options}`
			"""
		);


		if (ChatCompletionCreationOperationDuration is not null)
			await Task.Delay(ChatCompletionCreationOperationDuration.Value);

		_logger?.Log(LogLevel.Debug, ChatCompletionCreatedLOG,
			"Chat completion created with Options {Options}. Previous message had content {Content}",
			Options, lastMessageContent);

		return result;
	}
}
