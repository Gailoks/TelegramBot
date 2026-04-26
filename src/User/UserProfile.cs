using System.Globalization;

namespace TelegramAIBot.User;

record class UserProfile(long Id)
{
    public CultureInfo CultureInfo { get; init; } = CultureInfo.InvariantCulture;

    public string TelemetryKey => Id.ToString(); 
}