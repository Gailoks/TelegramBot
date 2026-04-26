namespace TelegramAIBot.Telegram.Sequences;

interface ISequenceRepository
{
    public void Load(TelegramClient telegramClient);

    public IEnumerable<TelegramSequence> List();
}
