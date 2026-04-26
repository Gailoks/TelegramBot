namespace TelegramAIBot.Memory;


interface IMemory
{
	public Task<float> CalculateCoherence(string A);
	public Task<IMemory> Translate(string A);
}