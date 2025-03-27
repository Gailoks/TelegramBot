
using System.Collections.Concurrent;
using System.Collections.Frozen;
using TelegramAIBot.Utils;
using static TelegramAIBot.User.IUserRepository;

namespace TelegramAIBot.User;

class UserRamRepository : IUserRepository
{
    private readonly ConcurrentDictionary<long, ProfileBox> _profiles = [];

	public async Task<ITransaction> BeginTransactionAsync(long id)
	{
		var box = _profiles.GetOrAdd(id, s => new ProfileBox(s));
		await box.Semaphore.WaitAsync();
		return new Transaction(this, box);
	}

	public async Task<AttemptResult<UserProfile>> TryRetrieveAsync(long id)
	{
		if (_profiles.TryGetValue(id, out var box))
		{
			await box.Semaphore.WaitAsync();
			if (box.Profile is not null)
				return AttemptResult<UserProfile>.Success(box.Profile);
		}
		
		return AttemptResult<UserProfile>.Fail();
	}

	private void ApplyTransactionResult(ProfileBox box)
	{
		box.Semaphore.Release();
	}


	private class Transaction : ITransaction
	{
		private readonly UserRamRepository _owner;
		private ProfileBox _box;
		private FutureAction _action = FutureAction.Rollback;


		public Transaction(UserRamRepository owner, ProfileBox box)
		{
			_owner = owner;
			_box = box;
		}


		public UserProfile Profile => _box.Profile ?? throw new InvalidOperationException("No profile exists");

		public bool HasObject => _box.Profile is not null;


		public ITransaction CreateBlack()
		{
			_box.Profile = new UserProfile(_box.Id);
			return this;
		}

		public ITransaction Drop()
		{
			_box.Profile = null;
			return this;
		}

		public ITransaction Update(Func<UserProfile, UserProfile> modification)
		{
			_box.Profile = modification(Profile);
			return this;
		}

		public ITransaction ShouldCommit()
		{
			_action = FutureAction.Commit;
			return this;
		}

		public ITransaction ShouldRollback()
		{
			_action = FutureAction.Rollback;
			return this;
		}

		public ValueTask DisposeAsync()
		{
			if (_action == FutureAction.Commit)
				_owner.ApplyTransactionResult(_box);

			return ValueTask.CompletedTask;
		}


		private enum FutureAction
		{
			Rollback,
			Commit
		}
	}

	private record ProfileBox(long Id)
	{
		public SemaphoreSlim Semaphore { get; } = new(1);

		public UserProfile? Profile { get; set; } = null;
	}
}