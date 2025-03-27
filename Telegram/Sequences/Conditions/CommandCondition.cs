using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram.Sequences.Conditions;

class CommandCondition(string command) : SequenceTrigger
{
    private readonly string _command = command;


    public override Task<bool> CheckCommandAsync(string command, Message rawMessage)
    {
        return Task.FromResult(command == _command);
    }
}
