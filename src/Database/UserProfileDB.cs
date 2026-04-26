using System.ComponentModel.DataAnnotations;

namespace TelegramAIBot.DataBase;

class UserProfileDB
{
    [Key]
    public int UserId { get; set; }
    public required string Language { get; set; }
}
