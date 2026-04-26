using TelegramAIBot.Utils;

namespace TelegramAIBot.User;

internal readonly struct UserRepositoryAccessor(IUserRepository repository, long id)
{
	private readonly IUserRepository _repository = repository;
	private readonly long _id = id;


	public long Id => _id;


	public Task<IUserRepository.ITransaction> BeginTransactionAsync() => _repository.BeginTransactionAsync(_id);

	public Task<AttemptResult<UserProfile>> TryRetrieveAsync() => _repository.TryRetrieveAsync(_id);
}
