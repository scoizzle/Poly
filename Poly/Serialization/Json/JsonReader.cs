namespace Poly.Serialization;

public sealed class JsonReader(StringView view) : IDataReader
{
    private readonly char[] EndOfMemberSearchChars = [',', '}', ']'];

    public bool IsDone => view.IsEmpty;

    public bool BeginArray(out int? numberOfMembers)
    {
        view.Consume(char.IsWhiteSpace);

        if (!view.Consume('['))
        {
            numberOfMembers = default;
            return false;
        }

        numberOfMembers = default;
        return true;
    }

    public bool EndArray()
    {
        view.Consume(char.IsWhiteSpace);

        return view.Consume(']');
    }

    public bool BeginMember(out StringView name)
    {
        view.Consume(char.IsWhiteSpace);

        if (!view.ExtractAndConsumeStringLiteral(out var sub))
        {
            name = default;
            return false;
        }

        name = new StringView(sub.String, sub.Index, sub.LastIndex);

        view.Consume(char.IsWhiteSpace);

        return view.Consume(':');
    }

    public bool BeginMember(DeserializeToObjectDelegate deserialize, out object name)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractAndConsumeStringLiteral(out var sub))
        {
            view.Consume(char.IsWhiteSpace);

            if (view.Consume(':'))
            {
                var view = new StringView(sub.String, sub.Index, sub.LastIndex);

                return deserialize(new StringReader(view), out name);
            }
        }

        name = default;
        return false;
    }

    public bool BeginMember<T>(DeserializeDelegate<T> deserialize, out T name)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractAndConsumeStringLiteral(out var sub))
        {
            view.Consume(char.IsWhiteSpace);

            if (view.Consume(':'))
            {
                var view = new StringView(sub.String, sub.Index, sub.LastIndex);

                return deserialize(new StringReader(view), out name);
            }
        }

        name = default;
        return false;
    }

    public bool BeginMember<TReader, T>(DeserializeDelegate<TReader, T> deserialize, [NotNullWhen(true)] out T? name) where TReader : class, IDataReader
    {
        view.Consume(char.IsWhiteSpace);

        if (this is not TReader reader)
        {
            name = default;
            return false;
        }

        if (deserialize(reader, out name))
        {
            view.Consume(char.IsWhiteSpace);

            if (view.Consume(':'))
            {
                return true;
            }
        }

        name = default;
        return false;
    }

    public bool EndValue()
    {
        view.Consume(char.IsWhiteSpace);

        return view.Consume(',');
    }

    public bool BeginObject()
    {
        view.Consume(char.IsWhiteSpace);

        return view.Consume('{');
    }

    public bool EndObject()
    {
        view.Consume(char.IsWhiteSpace);

        return view.Consume('}');
    }

    public bool Null()
    {
        view.Consume(char.IsWhiteSpace);

        return view.Consume("null");
    }

    public bool Char(out char value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractStringLiteral(out var sub) && sub.Length == 1 && sub.First.HasValue)
        {
            view.Consume(sub.Length);
            value = sub.First.Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool String(out string value)
    {
        if (StringView(out var view))
        {
            value = view.ToString();
            return true;
        }

        value = default;
        return Null();
    }

    public bool StringView(out StringView value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractAndConsumeStringLiteral(out var sub))
        {
            value = new StringView(sub.String, sub.Index, sub.LastIndex);
            return true;
        }

        value = default;
        return false;
    }

    public bool Boolean(out bool value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Float32(out float value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;

    }

    public bool Float64(out double value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Decimal(out decimal value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int8(out sbyte value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int16(out short value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int32(out int value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int64(out long value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt8(out byte value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt16(out ushort value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt32(out uint value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt64(out ulong value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractUntilAny(EndOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool DateTime(out DateTime value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractStringLiteral(out var sub))
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length + 2);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool TimeSpan(out TimeSpan value)
    {
        view.Consume(char.IsWhiteSpace);

        if (view.ExtractStringLiteral(out var sub))
        {
            if (sub.TryParse(out value))
            {
                view.Consume(sub.Length + 2);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Read<T>([NotNullWhen(returnValue: true)] out T? value) where T : ISpanParsable<T>
    {
        view.Consume(char.IsWhiteSpace);

        StringView sub;

        if (view.First == '"' && view.ExtractStringLiteral(out sub) && sub.TryParse(out value))
        {
            view.Consume(sub.Length + 2);
            return true;
        }
        else
        if (view.TryExtractUntilAny(values: EndOfMemberSearchChars, slice: out sub) && sub.TryParse(out value))
        {
            view.Consume(sub.Length);
            return true;
        }

        value = default;
        return false;
    }
}
