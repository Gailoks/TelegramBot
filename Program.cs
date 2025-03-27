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
			ReadableTimeSpan.EnableConfigurationBinding();

			var config = new ConfigurationBuilder()
				.AddInMemoryCollection(new Dictionary<string, string?>()
				{
					["Telegram:Token"] = Environment.GetEnvironmentVariable("TelegramAIBot_TGToken"),
					["AI:OpenAI:Token"] = Environment.GetEnvironmentVariable("TelegramAIBot_OpenAIToken")
				})
				.AddJsonFile("config.json")
				.Build();

            var serviceCollection = new ServiceCollection()
                .AddSingleton<ISequenceRepository, ReflectionSequenceRepository>()
                .AddTransient<ITelegramEventHandler, SequenceProcessor>()
                .AddSingleton<ITelegramSequenceModule, TelegramModule>()
                .AddSingleton<IUserRepository, UserRamRepository>()
                .AddDbContext<UserContextDB>((options) => options.UseNpgsql())
                
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

            var telegramClient = services.GetRequiredService<TelegramClient>();

            var repository = services.GetRequiredService<ISequenceRepository>();
            repository.Load(telegramClient);

            telegramClient.Start();

            Thread.Sleep(-1);
        }
    }
}