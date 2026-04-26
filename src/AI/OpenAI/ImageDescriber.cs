using Newtonsoft.Json.Linq;
using TelegramAIBot.AI.Abstractions;

namespace TelegramAIBot.AI.OpenAI;

internal sealed class ImageDescriber : IImageDescriber
{
	private readonly OpenAIClient _client;


	public ImageDescriber(OpenAIClient client)
	{
		_client = client;
	}


	public async Task<string> DescribeAsync(byte[] imageBytes, string? prompt = null, string mimeType = "image/jpeg")
	{
		if (imageBytes.Length == 0)
			throw new ArgumentException("Image data must not be empty.", nameof(imageBytes));

		var dataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
		var request = new
		{
			model = _client.InternalConfiguration.VisionModelName ?? _client.InternalConfiguration.ModelName,
			messages = new object[]
			{
				new
				{
					role = "system",
					content = "You describe images clearly, accurately, and concisely."
				},
				new
				{
					role = "user",
					content = new object[]
					{
						new
						{
							type = "text",
							text = prompt ?? "Describe this image."
						},
						new
						{
							type = "image_url",
							image_url = new
							{
								url = dataUrl,
								detail = "auto"
							}
						}
					}
				}
			}
		};

		var response = await _client.SendMessageAsync<JObject>(_client.InternalConfiguration.ChatCompletionEndpoint, request, HttpMethod.Post);
		var contentToken = response.ResponseBody["choices"]?[0]?["message"]?["content"];
		var content = contentToken switch
		{
			{ Type: JTokenType.String } => contentToken.Value<string>(),
			{ Type: JTokenType.Array } => string.Concat(contentToken
				.Children<JObject>()
				.Select(part => part["text"]?.Value<string>())
				.Where(text => string.IsNullOrWhiteSpace(text) == false)),
			_ => null
		};

		return string.IsNullOrWhiteSpace(content)
			? throw new InvalidOperationException("OpenAI image description response did not contain text content.")
			: content;
	}
}
