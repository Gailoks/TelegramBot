
using System.Reflection;

namespace TelegramAIBot.Telegram.Sequences;

class ReflectionSequenceRepository(IEnumerable<ITelegramSequenceModule> modules) : ISequenceRepository
{
    private readonly IEnumerable<ITelegramSequenceModule> _modules = modules;
    private readonly List<TelegramSequence> _sequences = [];


    public void Load(TelegramClient telegramClient)
    {
        foreach (var module in _modules)
        {
            var result = module
                .GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(s => new { Method = s, Attribute = s.GetCustomAttribute<TelegramSequenceAttribute>() })
                .Where(s => s.Attribute is not null)
                .Select(s =>
                {
                    module.BindClient(telegramClient);
                    var sequenceDelegate = s.Method.CreateDelegate<TelegramSequenceDelegate>(module);
                    return new TelegramSequence(sequenceDelegate, s.Attribute!.Trigger);
                });

            _sequences.AddRange(result);
        }
    }

    public IEnumerable<TelegramSequence> List()
    {
        return _sequences;
    }
}