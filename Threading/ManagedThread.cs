using System.Collections.Concurrent;

namespace TelegramAIBot.Threading;

internal sealed class ManagedThread : IDisposable
{
	private readonly ConcurrentQueue<ThreadTask> _queue = new();
	private readonly Thread _thread;
	private readonly AutoResetEvent _enqueueEvent = new(false);
	private bool _isDisposing = false;


	public ManagedThread(string internalThreadName = "ManagedThread thread")
	{
		_thread = new Thread(ThreadWorker);
		_thread.Name = internalThreadName;
	}


	public bool IsInside => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;


	public void Dispose()
	{
		_isDisposing = true;
		_enqueueEvent.Set();
		_thread.Join();
	}

	public void Start()
	{
		_thread.Start();
	}

	public void EnqueueTask(Action<object?> action, object? parameter, EventWaitHandle? completionEvent = null)
	{
		_queue.Enqueue(new ThreadTask(action, parameter, completionEvent));
		_enqueueEvent.Set();
	}

	private void ThreadWorker()
	{
		while (true)
		{
			_enqueueEvent.WaitOne();
			if (_isDisposing)
				return;

			while (_queue.TryDequeue(out var task))
			{
				task.Execute();
			}
		}
	}


	private record ThreadTask(Action<object?> Action, object? Parameter, EventWaitHandle? CompletionEvent)
	{
		public void Execute()
		{
			try
			{
				Action(Parameter);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString()); //TODO: log it properly
			}
			finally
			{
				CompletionEvent?.Set();
			}
		}
	}
}
