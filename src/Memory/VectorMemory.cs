using TelegramAIBot.AI.OpenAI;

namespace TelegramAIBot.Memory;

internal sealed class VectorMemory : IMemory
{
	private readonly float[] _embedding;
	private readonly Vectorizer _vectorizer;


	private VectorMemory(float[] embedding, Vectorizer vectorizer)
	{
		_embedding = embedding;
		_vectorizer = vectorizer;
	}


	public static async Task<VectorMemory> CreateAsync(string data, Vectorizer vectorizer)
	{
		var embedding = await vectorizer.Vectorize(data);
		return new VectorMemory(embedding, vectorizer);
	}


	public async Task<float> CalculateCoherence(string other)
	{
		var queryEmbedding = await _vectorizer.Vectorize(other);
		return CosineSimilarity(_embedding, queryEmbedding);
	}


	public Task<IMemory> Translate(string data)
	{
		return TranslateAsync(data);
	}


	private async Task<IMemory> TranslateAsync(string data)
	{
		return await CreateAsync(data, _vectorizer);
	}


	private static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
	{
		if (left.Count != right.Count)
			throw new InvalidOperationException("Embedding vectors must have the same length.");

		double dot = 0;
		double leftMagnitude = 0;
		double rightMagnitude = 0;

		for (var i = 0; i < left.Count; i++)
		{
			var leftValue = left[i];
			var rightValue = right[i];

			dot += leftValue * rightValue;
			leftMagnitude += leftValue * leftValue;
			rightMagnitude += rightValue * rightValue;
		}

		if (leftMagnitude == 0 || rightMagnitude == 0)
			return 0;

		return (float)(dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude)));
	}
}
