using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram.Sequences;

delegate IAsyncEnumerator<WaitCondition> TelegramSequenceDelegate(Message message, SequenceTrigger trigger);
