using Telegram.Bot.Types;

namespace TelegramAIBot.Telegram.Sequences;
class SequenceProcessor(ISequenceRepository repository) : ITelegramEventHandler
{
    private readonly Dictionary<long, UserState> _userStates = [];
    private readonly ISequenceRepository _repository = repository;


    public async Task HandleButtonAsync(Message message, string data)
    {
        var state = GetUserState(message.Chat.Id);

        if (state.ActiveSequence is not null && await state.ActiveSequence.Current.CheckButtonAsync(message, data))
        {
            await PromoteSequenceAsync(state);
            return;
        }

        var newSequence = await PullTriggersAsync(_repository.List(), s => s.CheckButtonAsync(message, data));
        if (newSequence is not null)
        {
            var enumerator = newSequence.Delegate(message, newSequence.Trigger);
            await InitializeSequenceAsync(state, enumerator);
        }
    }

    public async Task HandleCommandAsync(string command, Message rawMessage)
    {
        var newSequence = await PullTriggersAsync(_repository.List(), s => s.CheckCommandAsync(command, rawMessage));
        if (newSequence is not null)
        {
            var enumerator = newSequence.Delegate(rawMessage, newSequence.Trigger);
            await InitializeSequenceAsync(GetUserState(rawMessage.Chat.Id), enumerator);
        }
    }
    

    public async Task HandleMessageAsync(Message message)
    {
        var state = GetUserState(message.Chat.Id);
        
        if (state.ActiveSequence is not null && await state.ActiveSequence.Current.CheckMessageAsync(message))
        {
            await PromoteSequenceAsync(state);
            return;
        }

        var newSequence = await PullTriggersAsync(_repository.List(), s => s.CheckMessageAsync(message));
        if (newSequence is not null)
        {
            var enumerator = newSequence.Delegate(message, newSequence.Trigger);
            await InitializeSequenceAsync(state, enumerator);
            return;
        }
    }

    private async Task<TelegramSequence?> PullTriggersAsync(IEnumerable<TelegramSequence> sequences, Func<SequenceTrigger, Task<bool>> checker)
    {
        var results = await Task.WhenAll(sequences.Select(async s => (s, await checker(s.Trigger))));
        return results.FirstOrDefault(s => s.Item2).s;
    }

    private UserState GetUserState(long chatId)
    {
        if (!_userStates.TryGetValue(chatId, out var userState))
        {
            userState = new();
            _userStates.Add(chatId, userState);
        }

        return userState;
    }

    private async Task AbortSequenceAsync(UserState state)
    { 
        await state.ActiveSequence!.DisposeAsync();
        state.ActiveSequence = null;
    }

    private async Task PromoteSequenceAsync(UserState state)
    {
        if (!await state.ActiveSequence!.MoveNextAsync())
            await AbortSequenceAsync(state);
    }

    private async Task InitializeSequenceAsync(UserState state, IAsyncEnumerator<WaitCondition> sequence)
    {
        if(state.ActiveSequence is not null)
            await AbortSequenceAsync(state);
        state.ActiveSequence = sequence;
        await PromoteSequenceAsync(state);
    }


    private class UserState
    {
        public IAsyncEnumerator<WaitCondition>? ActiveSequence { get; set; }
    }
}