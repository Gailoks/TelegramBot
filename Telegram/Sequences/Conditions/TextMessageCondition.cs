using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram.Sequences.Conditions;

class TextMessageCondition : SequenceTrigger
{
    private Message? _capturedMessage = null;


    public Message CapturedMessage => _capturedMessage ??
        throw new InvalidOperationException("Enable to get property before waiting");


    public override Task<bool> CheckMessageAsync(Message message)
    {
        _capturedMessage = message;
        return Task.FromResult(true);
    }
}
