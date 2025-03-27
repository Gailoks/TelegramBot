namespace TelegramAIBot.User;

internal static class UserRepositoryExtensions
{
	public async static Task<IUserRepository.ITransaction> BeginModifyTransactionAsync(this IUserRepository self, long id)
	{
		var result = await self.BeginTransactionAsync(id);
		return result.RequireCreated();
	}

	public async static Task<UserProfile> RetrieveAsync(this IUserRepository self, long id)
	{
		var result = await self.TryRetrieveAsync(id);

		if (result.IsSuccess == false)
			throw new KeyNotFoundException($"Enable to retrieve. No profile for {id}");

		return result.Value;
	}

	public async static Task<IUserRepository.ITransaction> BeginModifyTransactionAsync(this UserRepositoryAccessor self)
	{
		var result = await self.BeginTransactionAsync();
		return result.RequireCreated();
	}

	public async static Task<UserProfile> RetrieveAsync(this UserRepositoryAccessor self)
	{
		var result = await self.TryRetrieveAsync();

		if (result.IsSuccess == false)
			throw new KeyNotFoundException($"Enable to retrieve. No profile for {self.Id}");

		return result.Value;
	}

	public static IUserRepository.ITransaction RequireCreated(this IUserRepository.ITransaction self)
	{
		if (self.HasObject == false)
			throw new InvalidOperationException($"Object in transaction is required");

		return self;
	}

	public static IUserRepository.ITransaction TryCreateBlank(this IUserRepository.ITransaction self)
	{
		if (self.HasObject == false)
			self.CreateBlack();

		return self;
	}

	public static IUserRepository.ITransaction CreateAndFill(this IUserRepository.ITransaction self, Func<UserProfile, UserProfile> modification)
	{
		return self
			.CreateBlack()
			.Update(modification);
	}

	public static IUserRepository.ITransaction TryCreateAndFill(this IUserRepository.ITransaction self, Func<UserProfile, UserProfile> modification)
	{
		if (self.HasObject == false)
			self.CreateBlack().Update(modification);

		return self;
	}
}
