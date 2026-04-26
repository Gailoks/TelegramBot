namespace TelegramAIBot.Threading
{
	internal sealed class ManagedThreadSynchronizationContext : SynchronizationContext
	{
		private readonly ManagedThread _target;


		public ManagedThreadSynchronizationContext(ManagedThread target)
		{
			_target = target;
		}


		public override SynchronizationContext CreateCopy()
		{
			return new ManagedThreadSynchronizationContext(_target);
		}

		public override void Post(SendOrPostCallback d, object? state)
		{
			_target.EnqueueTask(new Action<object?>(d), state);
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			if (_target.IsInside)
			{
				d(state);
			}
			else
			{
				var resetEvent = new AutoResetEvent(false);
				_target.EnqueueTask(new Action<object?>(d), state, resetEvent);
				resetEvent.Set();
			}
		}

		public void SendToThread()
		{
			Send(action, this);


			static void action(object? self)
			{
				SetSynchronizationContext((SynchronizationContext)self!);
			}
		}

		public static void UseInThread(ManagedThread target)
		{
			var context = new ManagedThreadSynchronizationContext(target);
			context.SendToThread();
		}
	}
}
