using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class DictionaryTypeMemberInterface<TKey, TValue> : IMemberInterface
{
    public DictionaryTypeMemberInterface(TKey key, ISystemTypeInterface<TValue> valueInterface) {
        Name = string.Empty;

        TypeInterface = valueInterface;

        Get = GetValueDelegate(key);
        Set = SetValueDelegate(key);

        Serialize = valueInterface.SerializeObject;
        Deserialize = valueInterface.DeserializeObject;
    }

    public string Name { get; }

    public ITypeInterface TypeInterface { get; }

    public Func<object, object?> Get { get; }

    public Action<object, object?> Set { get; }

    public SerializeObjectDelegate Serialize { get; }

    public DeserializeObjectDelegate Deserialize { get; }

    private static Func<object, object?> GetValueDelegate(TKey key) =>
        (obj) => obj is IDictionary<TKey, TValue> dictionary && dictionary?.TryGetValue(key, out var value) == true 
            ? value 
            : default;

    private static Action<object, object?> SetValueDelegate(TKey key) =>
        (obj, value) => {
            if (obj is IDictionary<TKey, TValue> dictionary && 
                value is TValue typed) {
                    dictionary[key] = typed;
                }
        };        
}