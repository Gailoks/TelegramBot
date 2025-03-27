using Newtonsoft.Json.Linq;
using System.Data;
using TelegramAIBot.AI.Abstractions;

namespace TelegramAIBot.AI.OpenAI
{
    internal class Chat : IAIChat
    {
        public const string CompletionEndpoint = "v1/chat/completions";


        private readonly OpenAIClient _client;


        public Chat(OpenAIClient client)
        {
            _client = client;
        }


        public ChatOptions Options { get; set; } = new();

        public IList<Message> Messages { get; } = new List<Message>();

        public Guid Id { get; } = Guid.NewGuid();


        public async Task<Message> CreateChatCompletionAsync()
        {
            ChatOptions options = Options;

            var messages = Messages;
            var apiMessages = messages
                .Select(msg =>
                {
                    return new
                    {
                        content = msg.Content,
                        role = msg.Role.ToString().ToLower()
                    };
                })
                .Prepend(new { content = options.SystemPrompt ?? "You are useful assistant", role = "system" })
                .ToArray();

            var request =
            new
            {
                model = _client.InternalConfiguration.ModelName,
                messages = apiMessages
            };

            var response = await _client.SendMessageAsync<JObject>(CompletionEndpoint, request, HttpMethod.Post);

            dynamic choice = response.ResponseBody;
            var content = (string)choice.choices[0].message.content;

            var message = new Message(MessageRole.Assistant, content);

            Messages.Add(message);

            return message;
        }

        public void ModifyOptions(Func<ChatOptions, ChatOptions> modification)
        {
            Options = modification(Options);
        }
    }
}
