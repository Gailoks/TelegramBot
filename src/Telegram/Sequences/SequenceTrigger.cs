using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram.Sequences;

abstract class SequenceTrigger : WaitCondition
{
    public virtual Task<bool> CheckCommandAsync(string command, Message rawMessage) => Task.FromResult(false);
}