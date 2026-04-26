global using Microsoft.Extensions.Options;
global using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelegramAIBot.AI.Gug;
using TelegramAIBot.AI.OpenAI;
using TelegramAIBot.Telegram;
using TomLonghurst.ReadableTimeSpan;
using TelegramAIBot.AI.Abstractions;
using TelegramAIBot.Telegram.Sequences;
using TelegramAIBot.Telemetry;
using TelegramAIBot.UserData;
using TelegramAIBot.User;
using TelegramAIBot.DataBase;
using Microsoft.EntityFrameworkCore;

namespace TelegramAIBot
{
	class Program
	{
		public static readonly EventId GugClientUsedLOG = new EventId(12, nameof(GugClientUsedLOG)).Form();


		public static void Main(string[] args)
		{

			while (true)
			{
				try
				{

					ReadableTimeSpan.EnableConfigurationBinding();

					var config = new ConfigurationBuilder()
						.AddInMemoryCollection(new Dictionary<string, string?>()
						{
							["Telegram:Token"] = Environment.GetEnvironmentVariable("TelegramAIBot_TGToken"),
							["AI:OpenAI:Token"] = Environment.GetEnvironmentVariable("TelegramAIBot_OpenAIToken"),
							["ConnectionStrings:UserContextDB"] = Environment.GetEnvironmentVariable("TelegramAIBot_Postgres")
						})
						.AddJsonFile("config.json")
						.Build();

					var userContextConnectionString = config.GetConnectionString("UserContextDB")
						?? throw new InvalidOperationException("Missing database connection string. Set TelegramAIBot_Postgres or ConnectionStrings:UserContextDB.");

					Console.WriteLine(
						"Startup config loaded. OpenAIServer={0}, ChatEndpoint={1}, EmbeddingsEndpoint={2}, Model={3}, ContextTokens={4}, CompressionMode={5}, DB={7}, Thinking={6}",
						config["AI:OpenAI:OpenAIServer"],
						config["AI:OpenAI:ChatCompletionEndpoint"],
						config["AI:OpenAI:EmbeddingsEndpoint"],
						config["AI:OpenAI:ModelName"],
						config["AI:OpenAI:ModelContextTokens"],
						config["AI:OpenAI:ContextCompressionMode"],
						config["AI:OpenAI:Thinking"],
						userContextConnectionString
					);

					var serviceCollection = new ServiceCollection()
						.AddSingleton<ISequenceRepository, ReflectionSequenceRepository>()
						.AddTransient<ITelegramEventHandler, SequenceProcessor>()
						.AddSingleton<ITelegramSequenceModule, TelegramModule>()
						.AddSingleton<IUserRepository, UserRamRepository>()
						.AddDbContext<UserContextDB>((options) => options.UseNpgsql(userContextConnectionString))

						.AddLogging(sb => sb.AddConsole().SetMinimumLevel(LogLevel.Trace))

						.Configure<TelegramClient.Options>(config.GetSection("Telegram"))
						.AddSingleton<TelegramClient>()

						.Configure<FileBasedTelemetryStorage.Options>(config.GetSection("Telemetry"))
						.AddTransient<ITelemetryStorage, FileBasedTelemetryStorage>()

						.AddLocalization(options =>
						{
							options.ResourcesPath = "Translations";
						})
					;


					var isGugClientImplementationUsed = args.Contains("--useGugClient");
					if (isGugClientImplementationUsed)
					{
						serviceCollection
							.Configure<GugClient.Configuration>(config.GetSection("AI:Gug"))
							.AddSingleton<IAIClient, GugClient>()
						;
					}
					else
					{
						serviceCollection
							.Configure<OpenAIClient.Configuration>(config.GetSection("AI:OpenAI"))
							.AddSingleton<IAIClient, OpenAIClient>()
						;
					}

					var services = serviceCollection.BuildServiceProvider();
					var logger = services.GetRequiredService<ILogger<Program>>();

					if (isGugClientImplementationUsed)
						logger.Log(LogLevel.Information, GugClientUsedLOG, "Using gug version of ai client");

					using (var scope = services.CreateScope())
					{
						var db = scope.ServiceProvider.GetRequiredService<UserContextDB>();
						Console.WriteLine("Bootstrapping telemetry schema...");
						db.EnsureSchemaAsync().GetAwaiter().GetResult();
						Console.WriteLine("Telemetry schema bootstrap completed.");
					}

					var telegramClient = services.GetRequiredService<TelegramClient>();
					Console.WriteLine("Telegram client resolved. Starting sequence repository and receiver.");

					var repository = services.GetRequiredService<ISequenceRepository>();
					repository.Load(telegramClient);

					telegramClient.Start();

					Thread.Sleep(-1);

				}
				catch (Exception e)
				{
					Console.WriteLine($"Error: {e}. \n Restarting in 5 seconds");
					Thread.Sleep(5);
				}
			}
		}
	}
}
