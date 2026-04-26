namespace TelegramAIBot.AI.OpenAI;

internal sealed class Vectorizer
{
	private readonly OpenAIClient _client;


	public Vectorizer(OpenAIClient client)
	{
		_client = client;
	}


	public async Task<float[]> Vectorize(string input)
	{
		var request = new
		{
			model = _client.InternalConfiguration.EmbeddingModelName,
			input,
			encoding_format = "float"
		};

		var response = await _client.SendMessageAsync<EmbeddingResponse>(_client.InternalConfiguration.EmbeddingsEndpoint, request, HttpMethod.Post);

		return response.ResponseBody.Data.FirstOrDefault()?.Embedding
			?? throw new InvalidOperationException("OpenAI embeddings response did not contain an embedding vector.");
	}


	private sealed record EmbeddingResponse(EmbeddingData[] Data);

	private sealed record EmbeddingData(float[] Embedding);
}
