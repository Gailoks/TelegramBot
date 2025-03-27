using Microsoft.Extensions.Logging;

namespace TelegramAIBot.UserData.InMemory
{
	internal sealed class InMemoryUserDataRepository : IUserDataRepository
	{
		public static readonly EventId StoreValueProvidedLOG = new EventId(11, nameof(StoreValueProvidedLOG)).Form();
		public static readonly EventId NewStoreCreatedLOG = new EventId(12, nameof(NewStoreCreatedLOG)).Form();
		public static readonly EventId ObjectHolderClosedLOG = new EventId(13, nameof(ObjectHolderClosedLOG)).Form();


		private readonly Dictionary<string, Store> _stores = [];
		private readonly SemaphoreSlim _lock = new(1);
		private readonly ILogger<InMemoryUserDataRepository>? _logger;


		public InMemoryUserDataRepository(ILogger<InMemoryUserDataRepository>? logger = null)
		{
			_logger = logger;
		}


		public ObjectHolder<TObject> Get<TObject>(string storageId)
			where TObject : notnull
		{
			TObject result;

			AutoResetEvent syncRoot;

			try
			{
				_lock.Wait();

				if (_stores.TryGetValue(storageId, out var value))
				{
					syncRoot = value.SyncRoot;
					result = (TObject)value.Object;
				}
				else
				{
					var store = Store.CreateDefault<TObject>();
					syncRoot = store.SyncRoot;
					_stores.Add(storageId, store);
					result = store.Read<TObject>();

					_logger?.Log(LogLevel.Debug, NewStoreCreatedLOG, "New storage with id {StorageId}", storageId);
				}

			}
			finally
			{
				_lock.Release();
			}


			syncRoot.WaitOne();

			var holderId = Guid.NewGuid();

			_logger?.Log(LogLevel.Trace, StoreValueProvidedLOG,
				"Value of storage {StorageId} provided with id {HolderId}. Current Value: {Value}",
				storageId, holderId, result);

			return new ObjectHolder<TObject>(result, storageId, FinalizeObjectHolder, holderId);
		}

		private void FinalizeObjectHolder<TObject>(ObjectHolder<TObject> holder, object parameter) where TObject : notnull
		{
			try
			{
				_lock.Wait();

				var storageId = (string)parameter;

				var store = _stores[storageId];

				store.Object = holder.Object;

				store.SyncRoot.Set();

				_logger?.Log(LogLevel.Trace, ObjectHolderClosedLOG,
					"Object holder with id {HolderId} of storage {StorageId} that now is free. New Value: {Value}",
					holder.Id, storageId, holder.Object);
			}
			finally
			{
				_lock.Release();
			}
		}


		private class Store
		{
			private Store(object obj)
			{
				Object = obj;
			}


			public object Object { get; set; }

			public AutoResetEvent SyncRoot { get; } = new(true);


			public static Store CreateDefault<TObject>() where TObject : notnull
			{
				return new Store(Activator.CreateInstance<TObject>());
			}

			public static Store CreateWithValue<TObject>(TObject obj) where TObject : notnull
			{
				return new Store(obj);
			}

			public TObject Read<TObject>() where TObject : notnull => (TObject)Object;
		}
	}
}
