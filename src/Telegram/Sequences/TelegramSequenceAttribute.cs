namespace TelegramAIBot.Telegram.Sequences;

class TelegramSequenceAttribute(Type type, params object[] args) : Attribute
{
    public SequenceTrigger Trigger { get; } = (SequenceTrigger)Activator.CreateInstance(type,args)!;
}
