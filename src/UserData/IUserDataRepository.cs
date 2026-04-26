namespace TelegramAIBot.UserData
{
	internal interface IUserDataRepository
	{
		public ObjectHolder<TObject> Get<TObject>(string storageId) where TObject : notnull;
	}
}
