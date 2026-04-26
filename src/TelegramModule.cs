using TelegramAIBot.Telegram.Sequences;
using TelegramAIBot.Telegram;
using TelegramAIBot.Telegram.Sequences.Conditions;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramAIBot.AI.Abstractions;
using TelegramAIBot.AI.Tools;
using TelegramAIBot.Telemetry;
using Microsoft.Extensions.Localization;
using TelegramAIBot.User;
using System.Collections.Concurrent;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.Enums;
using System.Globalization;
using System.Threading;
using Newtonsoft.Json.Linq;
using TGMessage = Telegram.Bot.Types.Message;
using AIMessage = TelegramAIBot.AI.Abstractions.Message;


namespace TelegramAIBot;

internal class TelegramModule(IAIClient aiClient, ITelemetryStorage telemetry, IStringLocalizer<TelegramModule> localizer, IUserRepository userRepository, ILogger<TelegramModule>? logger = null) : ITelegramSequenceModule
{
	public static readonly IReadOnlyCollection<string> SupportedLanguages = ["en", "ru"];


	private readonly IAIClient _aiClient = aiClient;
	private readonly ITelemetryStorage _telemetry = telemetry;
	private readonly IStringLocalizer<TelegramModule> _localizer = localizer;
	private readonly IUserRepository _userRepository = userRepository;
	private readonly ILogger<TelegramModule>? _logger = logger;
	private readonly TelegramAIBot.AI.Abstractions.IImageDescriber? _imageDescriber = aiClient.CreateImageDescriber();

	private readonly ConcurrentDictionary<long, Session> _sessions = [];
	private readonly ConcurrentDictionary<long, CancellationTokenSource> _sessionRevocationTokens = [];
	private TelegramClient? _client;


	private TelegramClient Client => _client ?? throw new InvalidOperationException("Bind before use");


	public void BindClient(TelegramClient client) => _client = client;


	[TelegramSequence(typeof(CommandCondition), "help")]
	public async IAsyncEnumerator<WaitCondition> ProcessCommandHelpAsync(TGMessage message, SequenceTrigger trigger)
	{
		_logger?.LogInformation("Help command received. UserId={UserId}", message.From?.Id);
		await Client.SendTextMessageAsync(message.Chat, _localizer.Get((await _userRepository.RetrieveAsync(message.From!.Id)).CultureInfo, "HelpContent"));
		yield break;
	}

	[TelegramSequence(typeof(CommandCondition), "system")]
	public async IAsyncEnumerator<WaitCondition> ProcessCommandSystem(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
		_logger?.LogInformation("System prompt command received. UserId={UserId}", userId);
		if (_sessions.TryGetValue(userId, out var session) == false)
			yield break;
		var profileTransaction = EnsureProfileInitialized(await session.User.BeginTransactionAsync(), message);
		var profile = profileTransaction.Profile;
		await profileTransaction.DisposeAsync();


		var systemWaitCondition = new TextMessageCondition();

		await Client.SendTextMessageAsync(userId, _localizer.Get(profile.CultureInfo, "WaitingNewSystemPrompt"));

		yield return systemWaitCondition;

		session.SystemPrompt = systemWaitCondition.CapturedMessage.Text;
		await _telemetry.SaveSessionAsync(session);
		_logger?.LogInformation("System prompt updated. UserId={UserId}, PromptChars={PromptChars}", userId, session.SystemPrompt?.Length ?? 0);

		await Client.SendTextMessageAsync(userId, _localizer.Get(profile.CultureInfo, "SystemPromptSet"));
	}

	[TelegramSequence(typeof(CommandCondition), "language")]
	public async IAsyncEnumerator<WaitCondition> ProcessCommandLanguageAsync(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
		_logger?.LogInformation("Language command received. UserId={UserId}", userId);
		CultureInfo initialProfileCulture;
		await using (var profileTransaction = await _userRepository.BeginTransactionAsync(userId))
		{
			EnsureProfileInitialized(profileTransaction, message);
			initialProfileCulture = profileTransaction.Profile.CultureInfo;
		}

		InlineKeyboardMarkup keyboard = new(SupportedLanguages.Select(InlineKeyboardButton.WithCallbackData));
		var menuMessage = await Client.SendTextMessageAsync(userId, _localizer.Get(initialProfileCulture, "LanguageSettings"), replyMarkup: keyboard);

		while (true)
		{
			var languageWaitCondition = new ButtonCondition(menuMessage.MessageId);
			yield return languageWaitCondition;

			await using var profileTransaction = await _userRepository.BeginTransactionAsync(userId);

			if (profileTransaction.Profile.CultureInfo.Name != languageWaitCondition.ButtonPressed)
			{
				profileTransaction
					.Update(s => s with { CultureInfo = new CultureInfo(languageWaitCondition.ButtonPressed) })
					.ShouldCommit();

				await Client.EditMessageTextAsync(userId, menuMessage.MessageId, _localizer.Get(profileTransaction.Profile.CultureInfo, "LanguageSettings"), replyMarkup: keyboard);
			}
		}
	}

	[TelegramSequence(typeof(CommandCondition), "start")]
	public async IAsyncEnumerator<WaitCondition> ProcessCommandStartAsync(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
		_logger?.LogInformation("Start command received. UserId={UserId}", userId);
		await using var profileTransaction = EnsureProfileInitialized(await _userRepository.BeginTransactionAsync(userId), message);
		
		if (_sessions.TryRemove(userId, out var old))
		{
			_logger?.LogInformation("Existing session removed before restart. UserId={UserId}", userId);
			await TrySaveSessionAsync(old, userId);
		}
		CancelScheduledRevoke(userId);
		_sessions[userId] = await CreateSessionAsync(userId, restoreFromTelemetry: true);

		await Client.SendTextMessageAsync(message.Chat, _localizer.Get(profileTransaction.Profile.CultureInfo, "SessionBegin"));
		_logger?.LogInformation("Start command completed. UserId={UserId}", userId);
		yield break;
	}


	private async Task<Session> CreateSessionAsync(long userId, bool restoreFromTelemetry)
	{
		try
		{
			_logger?.LogInformation("Creating session. UserId={UserId}, Restore={Restore}", userId, restoreFromTelemetry);
			var chat = _aiClient.CreateChat(CreateToolsForUser(userId));
			string? snapshotJson = null;
			if (restoreFromTelemetry)
			{
				try
				{
					snapshotJson = await _telemetry.TryLoadLatestPayloadAsync(userId.ToString());
				}
				catch (Exception ex)
				{
					_logger?.LogWarning(ex, "Telemetry restore skipped. UserId={UserId}", userId);
				}

				if (string.IsNullOrWhiteSpace(snapshotJson) == false)
				{
					var snapshot = JObject.Parse(snapshotJson);
					_logger?.LogInformation("Restoring telemetry snapshot into session. UserId={UserId}, SnapshotChars={SnapshotChars}", userId, snapshotJson.Length);

					chat.Options = new ChatOptions(
						snapshot["system"]?.Value<string>(),
						snapshot["contextSummary"]?.Value<string>()
					);

					var messages = snapshot["messages"] as JArray;
					if (messages is not null)
					{
						foreach (var item in messages.Children<JObject>())
						{
							var roleText = item["role"]?.Value<string>() ?? "user";
							var content = item["content"]?.Value<string>() ?? string.Empty;

							if (Enum.TryParse<MessageRole>(roleText, true, out var role))
								chat.Messages.Add(new AIMessage(role, content));
						}
					}
				}
			}

			_logger?.LogInformation("Session ready. UserId={UserId}, RestoredMessages={MessageCount}", userId, chat.Messages.Count);
			return new Session(chat, new UserRepositoryAccessor(_userRepository, userId));
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Fresh session fallback. UserId={UserId}", userId);
			return new Session(_aiClient.CreateChat(CreateToolsForUser(userId)), new UserRepositoryAccessor(_userRepository, userId));
		}
	}


	private Task<Session> CreateFreshSessionAsync(long userId)
	{
		return CreateSessionAsync(userId, restoreFromTelemetry: false);
	}


	private IReadOnlyCollection<ToolDefinition> CreateToolsForUser(long userId)
	{
		_logger?.LogDebug("Building tool set for user. UserId={UserId}", userId);
		return ToolSet.CreateForUser(_telemetry, userId.ToString(), duration => ScheduleRevokeAsync(userId, duration));
	}


	private Task ScheduleRevokeAsync(long userId, TimeSpan duration)
	{
		_logger?.LogInformation("Scheduling revoke. UserId={UserId}, Duration={Duration}", userId, duration);
		CancelScheduledRevoke(userId);

		var cts = new CancellationTokenSource();
		_sessionRevocationTokens[userId] = cts;

		_ = Task.Run(async () =>
		{
			try
			{
				_logger?.LogDebug("Revoke timer armed. UserId={UserId}, Duration={Duration}", userId, duration);
				await Task.Delay(duration, cts.Token);
				if (cts.Token.IsCancellationRequested)
					return;

				if (_sessions.TryRemove(userId, out var session))
				{
					_logger?.LogInformation("Revoke timer fired. UserId={UserId}", userId);
					await TrySaveSessionAsync(session, userId);
					_sessions[userId] = await CreateFreshSessionAsync(userId);
					var prompt = await BuildReopenPromptAsync(userId, duration);
					await Client.SendTextMessageAsync(userId, prompt);
				}
			}
			catch (OperationCanceledException)
			{
				_logger?.LogDebug("Revoke timer cancelled. UserId={UserId}", userId);
			}
			finally
			{
				if (_sessionRevocationTokens.TryGetValue(userId, out var current) && ReferenceEquals(current, cts))
					_sessionRevocationTokens.TryRemove(userId, out _);

				cts.Dispose();
			}
		});

		return Task.CompletedTask;
	}


	private void CancelScheduledRevoke(long userId)
	{
		if (_sessionRevocationTokens.TryRemove(userId, out var cts))
		{
			_logger?.LogInformation("Cancelling scheduled revoke. UserId={UserId}", userId);
			cts.Cancel();
			cts.Dispose();
		}
	}


	private static string FormatDuration(TimeSpan duration)
	{
		return duration.TotalHours >= 1
			? $"{(int)duration.TotalHours}h"
			: $"{(int)duration.TotalMinutes}m";
	}


	private async Task<string> BuildReopenPromptAsync(long userId, TimeSpan duration)
	{
		try
		{
			_logger?.LogInformation("Building reopen prompt. UserId={UserId}, Duration={Duration}", userId, duration);
			var chat = _aiClient.CreateChat();
			chat.Options = new ChatOptions(
				"You are a concise writing assistant. A writing session has just ended. Ask the user one short friendly question about what they would like to write about next.",
				null
			);
			chat.Messages.Add(new AIMessage(MessageRole.User, "Write only one short question."));
			var message = await chat.CreateChatCompletionAsync();
			if (string.IsNullOrWhiteSpace(message.Content) == false)
				return message.Content;
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Failed to build AI reopen prompt. UserId={UserId}", userId);
		}

		return $"Session revoked after {FormatDuration(duration)}. What would you like to write about next?";
	}


	[TelegramSequence(typeof(TextMessageCondition))]
	public async IAsyncEnumerator<WaitCondition> ProcessTextMessage(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
		_logger?.LogInformation("Text message received. UserId={UserId}, Length={Length}", userId, message.Text?.Length ?? 0);
		if (_sessions.TryGetValue(userId, out var session) == false)
		{
			_logger?.LogWarning("No active session for text message. Creating a new one. UserId={UserId}", userId);
			session = await CreateSessionAsync(userId, restoreFromTelemetry: true);
			_sessions[userId] = session;
			_logger?.LogInformation("Auto-created session for text message. UserId={UserId}", userId);
		}

		var question = message.Text!;
		var task = session.AskAsync(question);
		_logger?.LogInformation("AI request started. UserId={UserId}, QuestionChars={QuestionChars}", userId, question.Length);

		while (true)
		{
			await Client.SendChatActionAsync(userId, ChatAction.Typing);
			await Task.WhenAny(task, Task.Delay(4000));
			if (task.IsCompleted)
				break;
		}

		await Client.SendTextMessageAsync(userId, task.Result); // TODO: for other types
		await TrySaveSessionAsync(session, userId);
		_logger?.LogInformation("AI response sent and session saved. UserId={UserId}", userId);
		yield break;
	}


	[TelegramSequence(typeof(PhotoCondition))]
	public async IAsyncEnumerator<WaitCondition> ProcessPhotoMessage(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
		_logger?.LogInformation(
			"Photo message received. UserId={UserId}, PhotoCount={PhotoCount}, HasCaption={HasCaption}, CaptionChars={CaptionChars}",
			userId,
			message.Photo?.Length ?? 0,
			string.IsNullOrWhiteSpace(message.Caption) == false,
			message.Caption?.Length ?? 0
		);

		var session = await GetOrCreateSessionAsync(userId);
		var prompt = string.IsNullOrWhiteSpace(message.Caption)
			? "Describe this image briefly and helpfully."
			: message.Caption!;

		try
		{
			if (_imageDescriber is null)
			{
				_logger?.LogWarning("No image describer configured. UserId={UserId}", userId);
				await Client.SendTextMessageAsync(userId, "I received the image, but this setup cannot inspect images yet.");
				yield break;
			}

			var photo = message.Photo!
				.OrderByDescending(p => p.FileSize ?? 0)
				.First();

			var bytes = await DownloadPhotoBytesAsync(photo);
			_logger?.LogInformation(
				"Photo bytes downloaded. UserId={UserId}, FileId={FileId}, Bytes={Bytes}",
				userId,
				photo.FileId,
				bytes.Length
			);

			var description = await _imageDescriber.DescribeAsync(bytes, prompt);
			_logger?.LogInformation(
				"Photo described. UserId={UserId}, DescriptionChars={DescriptionChars}",
				userId,
				description.Length
			);

			await Client.SendTextMessageAsync(userId, description);
			await TrySaveSessionAsync(session, userId);
			yield break;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Photo processing failed. UserId={UserId}", userId);
			await Client.SendTextMessageAsync(userId, "I got the image, but I couldn't analyze it right now.");
			yield break;
		}
	}


	private async Task TrySaveSessionAsync(Session session, long userId)
	{
		try
		{
			_logger?.LogInformation("Saving session snapshot. UserId={UserId}", userId);
			await _telemetry.SaveSessionAsync(session);
			_logger?.LogInformation("Session snapshot saved. UserId={UserId}", userId);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Telemetry save skipped. UserId={UserId}", userId);
		}
	}


	private async Task<Session> GetOrCreateSessionAsync(long userId)
	{
		if (_sessions.TryGetValue(userId, out var session))
			return session;

		_logger?.LogWarning("No active session. Auto-creating session. UserId={UserId}", userId);
		session = await CreateSessionAsync(userId, restoreFromTelemetry: true);
		_sessions[userId] = session;
		_logger?.LogInformation("Auto-created session. UserId={UserId}", userId);
		return session;
	}


	private async Task<byte[]> DownloadPhotoBytesAsync(PhotoSize photo)
	{
		var file = await Client.GetFileAsync(photo.FileId);
		if (string.IsNullOrWhiteSpace(file.FilePath))
			throw new InvalidOperationException("Telegram file path is missing.");

		await using var memoryStream = new MemoryStream();
		await Client.DownloadFileAsync(file.FilePath, memoryStream);
		return memoryStream.ToArray();
	}

	private static IUserRepository.ITransaction EnsureProfileInitialized(IUserRepository.ITransaction transaction, TGMessage message)
	{
		return transaction
			.TryCreateAndFill(createUserProfileInitializer(message))
			.ShouldCommit();

		static Func<UserProfile, UserProfile> createUserProfileInitializer(TGMessage message)
		{
			UserProfile modification(UserProfile value) => value with { CultureInfo = message.GetUserLocale() };
			return modification;
		}
	}


#if DEBUG
	[TelegramSequence(typeof(CommandCondition), "drop")]
	public async IAsyncEnumerator<WaitCondition> DropUser(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
		var profileTransaction = await _userRepository.BeginTransactionAsync(userId);
		if (profileTransaction.HasObject)
		{
			await profileTransaction
				.Drop()
				.ShouldCommit()
				.DisposeAsync();

			await Client.SendTextMessageAsync(userId, "DEBUG: User profile dropped");
		}
		else
		{
			await profileTransaction.ShouldRollback().DisposeAsync();
			await Client.SendTextMessageAsync(userId, "DEBUG: No user profile has been created");
		}

		yield break;
	}
#endif
}
