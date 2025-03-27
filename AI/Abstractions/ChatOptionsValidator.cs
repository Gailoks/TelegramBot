using FluentValidation;

namespace TelegramAIBot.AI.Abstractions
{
	internal sealed class ChatOptionsValidator : AbstractValidator<ChatOptions>
	{
		public ChatOptionsValidator()
		{
			RuleFor(s => s.SystemPrompt).MaximumLength(500).When(s => s.SystemPrompt is not null);
		}
	}
}
