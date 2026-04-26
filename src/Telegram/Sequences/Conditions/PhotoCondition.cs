using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram.Sequences.Conditions;

class PhotoCondition : SequenceTrigger
{
	private Message? _capturedMessage = null;


	public Message CapturedMessage => _capturedMessage ??
	   throw new InvalidOperationException("Enable to get property before waiting");


	public override Task<bool> CheckMessageAsync(Message message)
	{
		if (message.Photo is not null)
		{
			_capturedMessage = message;
			return Task.FromResult(true);
		}
		else 
			return Task.FromResult(false);
	}
}
