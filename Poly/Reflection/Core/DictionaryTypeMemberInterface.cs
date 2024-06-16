using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class DictionaryTypeMemberInterface<TKey, TValue> : IMemberAdapter
{
    public DictionaryTypeMemberInterface(TKey key, ISystemTypeAdapter<TValue> valueInterface)
    {
        Name = StringView.Empty;

        TypeInterface = valueInterface;

        Get = GetValueDelegate(key);
        Set = SetValueDelegate(key);
    }

    public StringView Name { get; }

    public ITypeAdapter TypeInterface { get; }

    public Func<object, object?> Get { get; }

    public Action<object, object?> Set { get; }

    private static Func<object, object?> GetValueDelegate(TKey key) =>
        (obj) => obj is IDictionary<TKey, TValue> dictionary && dictionary?.TryGetValue(key, out var value) == true
            ? value
            : default;

    private static Action<object, object?> SetValueDelegate(TKey key) =>
        (obj, value) =>
        {
            if (obj is IDictionary<TKey, TValue> dictionary &&
                value is TValue typed)
            {
                dictionary[key] = typed;
            }
        };
}