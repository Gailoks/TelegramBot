using TelegramAIBot.Telegram.Sequences;
using TelegramAIBot.Telegram;
using TelegramAIBot.Telegram.Sequences.Conditions;
using Telegram.Bot;
using TelegramAIBot.AI.Abstractions;
using TelegramAIBot.Telemetry;
using Microsoft.Extensions.Localization;
using TelegramAIBot.User;
using System.Collections.Concurrent;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.Enums;
using System.Globalization;
using TGMessage = Telegram.Bot.Types.Message;


namespace TelegramAIBot;

internal class TelegramModule(IAIClient aiClient, ITelemetryStorage telemetry, IStringLocalizer<TelegramModule> localizer, IUserRepository userRepository) : ITelegramSequenceModule
{
	public static readonly IReadOnlyCollection<string> SupportedLanguages = ["en", "ru"];


	private readonly IAIClient _aiClient = aiClient;
	private readonly ITelemetryStorage _telemetry = telemetry;
	private readonly IStringLocalizer<TelegramModule> _localizer = localizer;
	private readonly IUserRepository _userRepository = userRepository;

	private readonly ConcurrentDictionary<long, Session> _sessions = [];
	private TelegramClient? _client;


	private TelegramClient Client => _client ?? throw new InvalidOperationException("Bind before use");


	public void BindClient(TelegramClient client) => _client = client;


	[TelegramSequence(typeof(CommandCondition), "help")]
	public async IAsyncEnumerator<WaitCondition> ProcessCommandHelpAsync(TGMessage message, SequenceTrigger trigger)
	{
		await Client.SendTextMessageAsync(message.Chat, _localizer.Get((await _userRepository.RetrieveAsync(message.From!.Id)).CultureInfo, "HelpContent"));
		yield break;
	}

	[TelegramSequence(typeof(CommandCondition), "system")]
	public async IAsyncEnumerator<WaitCondition> ProcessCommandSystem(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
		if (_sessions.TryGetValue(userId, out var session) == false)
			yield break;
		var profileTransaction = EnsureProfileInitialized(await session.User.BeginTransactionAsync(), message);
		var profile = profileTransaction.Profile;
		await profileTransaction.DisposeAsync();


		var systemWaitCondition = new TextMessageCondition();

		await Client.SendTextMessageAsync(userId, _localizer.Get(profile.CultureInfo, "WaitingNewSystemPrompt"));

		yield return systemWaitCondition;

		session.SystemPrompt = systemWaitCondition.CapturedMessage.Text;

		await Client.SendTextMessageAsync(userId, _localizer.Get(profile.CultureInfo, "SystemPromptSet"));
	}

	[TelegramSequence(typeof(CommandCondition), "language")]
	public async IAsyncEnumerator<WaitCondition> ProcessCommandLanguageAsync(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
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
		await using var profileTransaction = EnsureProfileInitialized(await _userRepository.BeginTransactionAsync(userId), message);
		
		if (_sessions.TryRemove(userId, out var old))
			await _telemetry.SaveSessionAsync(old);
		_sessions.TryAdd(userId, new(_aiClient.CreateChat(), new UserRepositoryAccessor(_userRepository, userId)));

		await Client.SendTextMessageAsync(message.Chat, _localizer.Get(profileTransaction.Profile.CultureInfo, "SessionBegin"));
		yield break;
	}


	[TelegramSequence(typeof(TextMessageCondition))]
	public async IAsyncEnumerator<WaitCondition> ProcessTextMessage(TGMessage message, SequenceTrigger trigger)
	{
		message.ExtractUserId(out var userId);
		if (_sessions.TryGetValue(userId, out var session) == false)
			yield break;

		var question = message.Text!;
		var task = session.AskAsync(question);

		while (true)
		{
			await Client.SendChatActionAsync(userId, ChatAction.Typing);
			await Task.WhenAny(task, Task.Delay(4000));
			if (task.IsCompleted)
				break;
		}

		await Client.SendTextMessageAsync(userId, task.Result); // TODO: for other types
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