using TelegramAIBot.Telegram.Sequences;
using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram.Sequences.Conditions;

class ButtonCondition : SequenceTrigger
{
    private string? _lastButtonPressed;
    private readonly long? _messageFilter;
    private readonly string? _targetButtonId;


    public ButtonCondition(string? targetButtonId = null)
    {
        _messageFilter = null;
        _targetButtonId = targetButtonId;
    }

    public ButtonCondition(long messageFilter, string? targetButtonId = null)
    {
        _messageFilter = messageFilter;
        _targetButtonId = targetButtonId;
    }

    public ButtonCondition(Message messageFilter, string? targetButtonId = null)
        : this(messageFilter.MessageId, targetButtonId) { }


    public string ButtonPressed => _lastButtonPressed ??
        throw new InvalidOperationException("Enable to get property before waiting");


    public override Task<bool> CheckButtonAsync(Message message, string data)
    {
        if (_messageFilter is not null && message.MessageId != _messageFilter)
            return Task.FromResult(false);

        if (_targetButtonId is not null && data != _targetButtonId)
            return Task.FromResult(false);

        _lastButtonPressed = data;

        return Task.FromResult(true);
    }
}
