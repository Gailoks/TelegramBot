namespace TelegramAIBot.AI.Abstractions;

internal interface IImageDescriber
{
	Task<string> DescribeAsync(byte[] imageBytes, string? prompt = null, string mimeType = "image/jpeg");
}
