using Newtonsoft.Json.Linq;
using TelegramAIBot.AI.Abstractions;
using TelegramAIBot.AI.Tools;

namespace TelegramAIBot.AI.OpenAI
{
	internal class Chat : IAIChat
	{
		private readonly OpenAIClient _client;
		private readonly IReadOnlyCollection<ToolDefinition> _tools;


		public Chat(OpenAIClient client, IReadOnlyCollection<ToolDefinition>? tools = null)
		{
			_client = client;
			_tools = tools ?? Array.Empty<ToolDefinition>();
		}


		public ChatOptions Options { get; set; } = new();

		public IList<Message> Messages { get; } = new List<Message>();

		public Guid Id { get; } = Guid.NewGuid();


		public async Task<Message> CreateChatCompletionAsync()
		{
			_client.Logger?.LogInformation(
				"Chat completion started. ChatId={ChatId}, Messages={MessageCount}, SystemPromptSet={HasSystemPrompt}, SummarySet={HasSummary}, ToolCount={ToolCount}",
				Id,
				Messages.Count,
				string.IsNullOrWhiteSpace(Options.SystemPrompt) == false,
				string.IsNullOrWhiteSpace(Options.ContextSummary) == false,
				_tools.Count
			);

			await CompactContextIfNeededAsync(Options);
			ChatOptions options = Options;

			var messages = Messages;
			var apiMessages = new List<object>();
			apiMessages.Add(new { content = options.SystemPrompt ?? "You are useful assistant", role = "system" });
			if (string.IsNullOrWhiteSpace(options.ContextSummary) == false)
				apiMessages.Add(new { content = "Conversation summary so far:\n" + options.ContextSummary, role = "system" });

			apiMessages.AddRange(messages
				.Select(msg =>
				{
					return new
					{
						content = msg.Content,
						role = msg.Role.ToString().ToLower()
					};
				})
				.Cast<object>());

			var tools = _tools.Count == 0
				? null
				: _tools.Select(tool => new
				{
					type = "function",
					function = new
					{
						name = tool.Name,
						description = tool.Description,
						parameters = tool.Parameters
					}
				}).ToArray();
			var toolChoice = _tools.Count == 0 ? null : "auto";

			while (true)
			{
				_client.Logger?.LogDebug(
					"Chat request iteration started. ChatId={ChatId}, ApiMessageCount={ApiMessageCount}, ToolCount={ToolCount}",
					Id,
					apiMessages.Count,
					_tools.Count
				);

				var request = new
				{
					model = _client.InternalConfiguration.ModelName,
					messages = apiMessages,
					tools,
					tool_choice = toolChoice
				};

				var response = await _client.SendMessageAsync<JObject>(_client.InternalConfiguration.ChatCompletionEndpoint, request, HttpMethod.Post);
				_client.Logger?.LogDebug(
					"Chat response received. ChatId={ChatId}, RawKeys={RawKeys}",
					Id,
					string.Join(",", response.ResponseBody.Properties().Select(p => p.Name))
				);

				var choice = response.ResponseBody["choices"]?[0]?["message"] as JObject
					?? throw new InvalidOperationException("OpenAI chat response did not contain a message.");

				var toolCalls = choice["tool_calls"] as JArray;
				if (toolCalls is not null && toolCalls.Count > 0)
				{
					_client.Logger?.LogInformation(
						"Chat tool calls requested. ChatId={ChatId}, ToolCallCount={ToolCallCount}",
						Id,
						toolCalls.Count
					);

					apiMessages.Add(new
					{
						role = "assistant",
						content = choice["content"]?.ToObject<object>(),
						tool_calls = toolCalls
					});

					foreach (var toolCall in toolCalls.Children<JObject>())
					{
						var toolName = toolCall["function"]?["name"]?.Value<string>()
							?? throw new InvalidOperationException("Tool call name is missing.");
						var toolArguments = toolCall["function"]?["arguments"]?.Value<string>() ?? "{}";
						var toolCallId = toolCall["id"]?.Value<string>()
							?? throw new InvalidOperationException("Tool call id is missing.");

						_client.Logger?.LogDebug(
							"Executing tool call. ChatId={ChatId}, ToolName={ToolName}, ToolCallId={ToolCallId}, Arguments={Arguments}",
							Id,
							toolName,
							toolCallId,
							Truncate(toolArguments, 1000)
						);

						var tool = _tools.FirstOrDefault(t => t.Name == toolName)
							?? throw new InvalidOperationException($"Unknown tool call requested: {toolName}");

						var toolResult = await tool.Handler(toolArguments);
						_client.Logger?.LogDebug(
							"Tool call completed. ChatId={ChatId}, ToolName={ToolName}, Result={Result}",
							Id,
							toolName,
							Truncate(toolResult, 1000)
						);

						apiMessages.Add(new
						{
							role = "tool",
							tool_call_id = toolCallId,
							content = toolResult
						});
					}

					continue;
				}

				var content = choice["content"]?.Value<string>()
					?? choice["content"]?.ToString();

				var message = new Message(MessageRole.Assistant, content ?? string.Empty);

				Messages.Add(message);
				_client.Logger?.LogInformation(
					"Chat completion finished. ChatId={ChatId}, ResponseLength={ResponseLength}",
					Id,
					message.Content.Length
				);

				return message;
			}
		}

		public void ModifyOptions(Func<ChatOptions, ChatOptions> modification)
		{
			Options = modification(Options);
		}


		private async Task CompactContextIfNeededAsync(ChatOptions options)
		{
			var config = _client.InternalConfiguration;
			if (config.ModelContextTokens <= 0)
				return;

			var beforeTokens = EstimateContextTokens(options, Messages);
			_client.Logger?.LogDebug(
				"Context compaction check. ChatId={ChatId}, Tokens={Tokens}, Limit={Limit}, Mode={Mode}, Messages={Messages}",
				Id,
				beforeTokens,
				config.ModelContextTokens,
				config.ContextCompressionMode,
				Messages.Count
			);

			while (EstimateContextTokens(options, Messages) > config.ModelContextTokens)
			{
				if (config.ContextCompressionMode == ContextCompressionMode.SlidingWindow)
				{
					if (Messages.Count == 0)
						break;

					_client.Logger?.LogInformation(
						"Sliding window compaction removed oldest message. ChatId={ChatId}, RemainingMessages={RemainingMessages}",
						Id,
						Messages.Count - 1
					);

					Messages.RemoveAt(0);
					continue;
				}

				if (await SummarizeOlderMessagesAsync(config) == false)
				{
					if (Messages.Count == 0)
						break;

					_client.Logger?.LogInformation(
						"Summary compaction failed, dropping oldest message. ChatId={ChatId}, RemainingMessages={RemainingMessages}",
						Id,
						Messages.Count - 1
					);

					Messages.RemoveAt(0);
				}
			}
		}


		private async Task<bool> SummarizeOlderMessagesAsync(OpenAIClient.Configuration config)
		{
			if (Messages.Count <= config.ContextMinimumRetainedMessages)
				return false;

			var removableCount = Math.Min(
				Messages.Count - config.ContextMinimumRetainedMessages,
				Math.Max(2, Messages.Count / 2)
			);

			if (removableCount <= 0)
				return false;

			var toSummarize = Messages.Take(removableCount).ToArray();
			var transcript = BuildTranscript(toSummarize, Options.ContextSummary);
			_client.Logger?.LogInformation(
				"Summarizing older messages. ChatId={ChatId}, RemovableCount={RemovableCount}, TranscriptChars={TranscriptChars}",
				Id,
				removableCount,
				transcript.Length
			);

			var summary = await SummarizeTranscriptAsync(transcript, config);
			if (string.IsNullOrWhiteSpace(summary))
				return false;

			if (summary.Length > config.ContextSummaryMaxCharacters)
				summary = summary[..config.ContextSummaryMaxCharacters];

			Options = Options with { ContextSummary = summary };
			_client.Logger?.LogInformation(
				"Context summary updated. ChatId={ChatId}, SummaryChars={SummaryChars}",
				Id,
				summary.Length
			);

			for (var i = 0; i < removableCount; i++)
				Messages.RemoveAt(0);

			return true;
		}


		private async Task<string> SummarizeTranscriptAsync(string transcript, OpenAIClient.Configuration config)
		{
			var request = new
			{
				model = config.ModelName,
				temperature = 0.2,
				messages = new object[]
				{
					new
					{
						role = "system",
						content = "Compress the conversation into a short factual summary. Keep important names, preferences, decisions, tasks, and tool results. Return only the summary text."
					},
					new
					{
						role = "user",
						content = transcript
					}
				}
			};

			var response = await _client.SendMessageAsync<JObject>(_client.InternalConfiguration.ChatCompletionEndpoint, request, HttpMethod.Post);
			return response.ResponseBody["choices"]?[0]?["message"]?["content"]?.Value<string>() ?? string.Empty;
		}


		private static string BuildTranscript(IEnumerable<Message> messages, string? existingSummary)
		{
			var builder = new System.Text.StringBuilder();

			if (string.IsNullOrWhiteSpace(existingSummary) == false)
			{
				builder.AppendLine("Previous summary:");
				builder.AppendLine(existingSummary);
				builder.AppendLine();
			}

			foreach (var message in messages)
				builder.AppendLine($"{message.Role}: {message.Content}");

			return builder.ToString();
		}


		private static int EstimateContextTokens(ChatOptions options, IEnumerable<Message> messages)
		{
			var totalChars = 0;

			if (string.IsNullOrWhiteSpace(options.SystemPrompt) == false)
				totalChars += options.SystemPrompt.Length;

			if (string.IsNullOrWhiteSpace(options.ContextSummary) == false)
				totalChars += options.ContextSummary.Length;

			foreach (var message in messages)
				totalChars += message.Content.Length;

			return Math.Max(1, totalChars / 4);
		}


		private static string Truncate(string value, int maxLength)
		{
			if (value.Length <= maxLength)
				return value;

			return value[..maxLength] + "...";
		}

	}
}
