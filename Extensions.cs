using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Telegram.Bot.Types;

namespace TelegramAIBot
{
	internal static class Extensions
	{
		public static EventId Form(this EventId id)
		{
			if (id.Name is null)
				return id;

			//Cuts 'LOG' postfix
			return new EventId(id.Id, id.Name[..^3]);
		}

		public static LocalizedString Get(this IStringLocalizer localizer, CultureInfo culture, string key, params object[] parameters)
		{
			var old = Thread.CurrentThread.CurrentUICulture;
			Thread.CurrentThread.CurrentUICulture = culture;
			var result = localizer[key, parameters];
			Thread.CurrentThread.CurrentUICulture = old;
			return result;
		}

		public static CultureInfo GetUserLocale(this Message message)
		{
			var raw = message.From?.LanguageCode;
			if (raw is null)
				return CultureInfo.InvariantCulture;
			else return new(raw);
		}

		public static void ExtractUserId(this Message message, out long userId)
		{
			userId = message.From!.Id;
		}
	}
}
