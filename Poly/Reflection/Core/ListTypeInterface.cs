using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class ListTypeInterface<TList, TElement> : ISystemTypeInterface<TList> 
    where TList : IList<TElement> 
{
    static readonly ISystemTypeInterface<TElement> ElementTypeInterface = TypeInterfaceRegistry.Get<TElement>()!;

    public ListTypeInterface()
    {
        Type = typeof(TList);
        SerializeObject = new SerializeDelegate<TList>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<TList>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out TList? list) where TReader : class, IDataReader
    {
		using var _ = Instrumentation.AddEvent();

        if (reader is null) {
            list = default;
            return false;
        }

        if (reader.BeginArray(out var __))
        {
            var instance = Activator.CreateInstance(typeof(TList));
            Guard.IsNotNull(instance);

            list = (TList)instance;

            while (!reader.IsDone)
            {
                if (!ElementTypeInterface.Deserialize(reader, out var value))
                    return false;

                list.Add(value);

                if (!reader.EndValue())
                    break;
            }

            return reader.EndArray();
        }

        list = default;
        return reader.Null();
    }

    public bool Serialize<TWriter>(TWriter writer, TList list) where TWriter : class, IDataWriter
    { 
		using var _ = Instrumentation.AddEvent();

        Guard.IsNotNull(writer);
        
        if (list is null) return writer.Null();

        if (!writer.BeginArray()) return false;

        foreach (var element in list)
        {
            if (!ElementTypeInterface.Serialize(writer, element))
                return false;

            if (!writer.EndValue())
                break;
        }

        return writer.EndArray();
    }
}