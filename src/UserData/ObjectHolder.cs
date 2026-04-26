namespace TelegramAIBot.UserData
{
	internal sealed class ObjectHolder<TObject> : IDisposable
	{
		private readonly object _parameter;
		private readonly Action<ObjectHolder<TObject>, object> _finalizer;


		public ObjectHolder(TObject obj, object parameter, Action<ObjectHolder<TObject>, object> finalizer, Guid id)
		{
			Object = obj;
			_parameter = parameter;
			_finalizer = finalizer;
			Id = id;
		}


		public TObject Object { get; set; }

		public Guid Id { get; }


		public void Dispose()
		{
			_finalizer(this, _parameter);
		}
	}
}
