using Poly.Serialization;

namespace Poly.Reflection.Core;
internal class Array<TElement> : ISystemTypeInterface<TElement[]>
{
    public Array()
    {
        Type = typeof(TElement[]);
        ElementTypeInterface = TypeInterfaceRegistry.Get<TElement>()!;
        SerializeObject = new SerializeDelegate<TElement[]>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<TElement[]>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public ISystemTypeInterface<TElement> ElementTypeInterface { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out TElement[]? value) where TReader : class, IDataReader
    {
		using var _ = Instrumentation.AddEvent();

        Guard.IsNotNull(reader);

        if (reader.BeginArray(out var numberOfMembers))
        {
            if (numberOfMembers.HasValue)
            {
                value = new TElement[numberOfMembers.Value];

                var memberNumber = 0;

                while (!reader.IsDone && memberNumber < numberOfMembers)
                {
                    ref var pos = ref value[memberNumber++];

                    if (!ElementTypeInterface.Deserialize(reader, out pos))
                    {
                        value = default;
                        return false;
                    }

                    if (!reader.EndValue())
                        break;
                }
                
                return true;
            }
            else {
                var list = new List<TElement>();

                while (!reader.IsDone)
                {
                    if (!ElementTypeInterface.Deserialize(reader, out var element))
                    {
                        value = default;
                        return false;
                    }

                    list.Add(element);

                    if (!reader.EndValue())
                        break;
                }

                if (reader.EndArray())
                {
                    value = new TElement[list.Count];
                    list.CopyTo(0, value, 0, list.Count);
                    return true;
                }
            }
        }

        value = default;
        return reader.Null();
    }

    public bool Serialize<TWriter>(TWriter writer, TElement[] value) where TWriter : class, IDataWriter
    { 
		using var _ = Instrumentation.AddEvent();

        Guard.IsNotNull(writer);
        
        if (value is null) return writer.Null();

        if (!writer.BeginArray()) return false;

        foreach (var element in value)
        {
            if (!ElementTypeInterface.Serialize(writer, element))
                return false;

            if (!writer.EndValue())
                break;
        }

        return writer.EndArray();
    }
}