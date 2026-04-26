namespace TelegramAIBot.Utils
{
	internal sealed class AttemptResult<TResult> where TResult : class
	{
		private readonly TResult? _value = null;


		private AttemptResult(TResult? value)
		{
			_value = value;
		}


		public bool IsSuccess => _value is not null;

		public TResult Value => _value ?? throw new InvalidOperationException("Enable to get value from failed AttemptResult object");


		public static bool operator true(AttemptResult<TResult> self) => self.IsSuccess;

		public static bool operator false(AttemptResult<TResult> self) => self.IsSuccess == false;

		public static implicit operator bool(AttemptResult<TResult> self) => self.IsSuccess;


		public static AttemptResult<TResult> Success(TResult value) => new(value);

		public static AttemptResult<TResult> Fail() => new(null);
	}
}
