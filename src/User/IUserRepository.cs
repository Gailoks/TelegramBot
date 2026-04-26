using TelegramAIBot.Utils;

namespace TelegramAIBot.User;

interface IUserRepository
{
	public Task<ITransaction> BeginTransactionAsync(long id);

	public Task<AttemptResult<UserProfile>> TryRetrieveAsync(long id);


	public interface ITransaction : IAsyncDisposable
	{
		public UserProfile Profile { get; }

		public bool HasObject { get; }


		public ITransaction Drop();

		public ITransaction CreateBlack();

		public ITransaction Update(Func<UserProfile, UserProfile> modification);

		public ITransaction ShouldCommit();

		public ITransaction ShouldRollback();
	}
}
