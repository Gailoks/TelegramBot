using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram;
interface ITelegramEventHandler
{
    public Task HandleMessageAsync(Message message);
    public Task HandleButtonAsync(Message message, string data);
    public Task HandleCommandAsync(string command, Message rawMessage);
}
