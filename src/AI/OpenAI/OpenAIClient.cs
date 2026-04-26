using System.Text;
using TelegramAIBot.AI.Abstractions;
using TelegramAIBot.AI.Tools;
using Newtonsoft.Json;

namespace TelegramAIBot.AI.OpenAI
{
	internal sealed class OpenAIClient : IAIClient
	{
		private readonly Configuration _configuration;
		private readonly ILogger<OpenAIClient>? _logger;
		private readonly HttpClient _httpClient = new();
		private readonly SemaphoreSlim _requestSync = new(3, 3);


		public Configuration InternalConfiguration => _configuration;
		internal ILogger<OpenAIClient>? Logger => _logger;


		public OpenAIClient(IOptions<Configuration> configuration, ILogger<OpenAIClient>? logger = null)
		{
			_configuration = configuration.Value;
			_logger = logger;
			_requestSync = new SemaphoreSlim(configuration.Value.RequestConcurrentLimit);

			_logger?.LogInformation(
				"OpenAI client configured. BaseUrl={BaseUrl}, ChatEndpoint={ChatEndpoint}, EmbeddingsEndpoint={EmbeddingsEndpoint}, Model={Model}, VisionModel={VisionModel}, ContextTokens={ContextTokens}, CompressionMode={CompressionMode}",
				_configuration.OpenAIServer,
				_configuration.ChatCompletionEndpoint,
				_configuration.EmbeddingsEndpoint,
				_configuration.ModelName,
				_configuration.VisionModelName ?? _configuration.ModelName,
				_configuration.ModelContextTokens,
				_configuration.ContextCompressionMode,
				_configuration.Thinking
			);
		}


		public async Task<ServerResponse<TResponse>> SendMessageAsync<TResponse>(string endpoint, object body, HttpMethod method, Dictionary<string, string>? headers = null)
			where TResponse : notnull
		{
			headers ??= [];
			headers.Add("Authorization", "Bearer " + _configuration.Token);

			var serializedBody = JsonConvert.SerializeObject(body, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
			var content = new StringContent(serializedBody, Encoding.UTF8, "application/json");

			var request = new HttpRequestMessage()
			{
				Content = content,
				Method = method,
				RequestUri = BuildEndpointUri(_configuration.OpenAIServer, endpoint)
			};

			_logger?.LogDebug(
				"OpenAI request prepared. Method={Method}, Endpoint={Endpoint}, ResolvedUri={ResolvedUri}, BodyBytes={BodyBytes}",
				method.Method,
				endpoint,
				request.RequestUri,
				Encoding.UTF8.GetByteCount(serializedBody)
			);

			foreach (var header in headers)
				request.Headers.Add(header.Key, header.Value);

			await _requestSync.WaitAsync();
			HttpResponseMessage response;
			try
			{
				response = await _httpClient.SendAsync(request);
			}
			finally { _requestSync.Release(); }

			var responseContent = await response.Content.ReadAsStringAsync();
			_logger?.LogDebug(
				"OpenAI response received. Endpoint={Endpoint}, StatusCode={StatusCode}, ContentBytes={ContentBytes}",
				endpoint,
				(int)response.StatusCode,
				Encoding.UTF8.GetByteCount(responseContent)
			);

			if (response.IsSuccessStatusCode == false)
			{
				_logger?.LogWarning(
					"OpenAI request failed. Endpoint={Endpoint}, StatusCode={StatusCode}, ResponseBody={ResponseBody}",
					endpoint,
					response.StatusCode,
					Truncate(responseContent, 2000)
				);
				throw new OpenAIApiException(endpoint, request, body, response, responseContent);
			}

			var responseAsObject = JsonConvert.DeserializeObject<TResponse>(responseContent) ?? throw new NullReferenceException();

			return new ServerResponse<TResponse>(responseAsObject, response);
		}

		public IAIChat CreateChat()
		{
			return new Chat(this);
		}


		public IAIChat CreateChat(IReadOnlyCollection<ToolDefinition>? tools)
		{
			return new Chat(this, tools);
		}


		public Vectorizer CreateVectorizer()
		{
			return new Vectorizer(this);
		}


		public IImageDescriber CreateImageDescriber()
		{
			return new ImageDescriber(this);
		}


		public class Configuration
		{
			public string OpenAIServer { get; init; } = "https://api.openai.com/";

			public string ChatCompletionEndpoint { get; init; } = "chat/completions";

			public string EmbeddingsEndpoint { get; init; } = "embeddings";

			public int RequestConcurrentLimit { get; init; } = 3;

			public required string Token { get; init; }

			public required string ModelName { get; init; }

			public int ModelContextTokens { get; init; } = 12000;

			public ContextCompressionMode ContextCompressionMode { get; init; } = ContextCompressionMode.SlidingWindow;

			public int ContextMinimumRetainedMessages { get; init; } = 6;

			public int ContextSummaryMaxCharacters { get; init; } = 4000;

			public string EmbeddingModelName { get; init; } = "text-embedding-3-small";

			public string? VisionModelName { get; init; } = null;

			public bool Thinking { get; init; } = false;
		}

		public record ServerResponse<TResponse>(TResponse ResponseBody, HttpResponseMessage RawServerResponse) where TResponse : notnull;


		private static Uri BuildEndpointUri(string baseUrl, string endpoint)
		{
			var normalizedBase = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
			var normalizedEndpoint = endpoint.StartsWith('/') ? endpoint[1..] : endpoint;

			return new Uri(new Uri(normalizedBase), normalizedEndpoint);
		}


		private static string Truncate(string value, int maxLength)
		{
			if (value.Length <= maxLength)
				return value;

			return value[..maxLength] + "...";
		}
	}
}
