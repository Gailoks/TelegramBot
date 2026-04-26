namespace TelegramAIBot.AI.OpenAI
{
	internal class OpenAIApiException : Exception
	{
		public OpenAIApiException(string endpoint, HttpRequestMessage requestMessage, object requestBody, HttpResponseMessage responseMessage, string responseMessageContent) :
			base($"Exception while sending message to OpenAI server, endpoint: {endpoint}")
		{
			Endpoint = endpoint;
			RequestMessage = requestMessage;
			RequestBody = requestBody;
			ResponseMessage = responseMessage;
			ResponseMessageContent = responseMessageContent;
		}


		public string Endpoint { get; }

		public HttpRequestMessage RequestMessage { get; }

		public object RequestBody { get; }

		public HttpResponseMessage ResponseMessage { get; }

		public string ResponseMessageContent { get; }
	}
}
