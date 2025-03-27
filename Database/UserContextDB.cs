using Microsoft.EntityFrameworkCore;

namespace TelegramAIBot.DataBase;

class UserContextDB(DbContextOptions<UserContextDB> dbContextOptions) : DbContext(dbContextOptions)
{
    public DbSet<UserProfileDB> UserProfiles { get; private set; }


}