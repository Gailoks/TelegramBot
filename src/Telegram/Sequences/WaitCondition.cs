using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram.Sequences;

abstract class WaitCondition
{
    public virtual Task<bool> CheckButtonAsync(Message message, string data) => Task.FromResult(false);
    public virtual Task<bool> CheckMessageAsync(Message message) => Task.FromResult(false);
}