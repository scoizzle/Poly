namespace Poly.Serialization;

public sealed class JsonReader(StringView view) : IDataReader
{
    private readonly char[] _endOfMemberSearchChars = [',', '}', ']'];
    private StringView _view = view;

    public bool IsDone => _view.IsEmpty;

    public bool BeginArray(out int? numberOfMembers)
    {
        _view.ConsumeWhitespace();

        if (!_view.Consume('['))
        {
            numberOfMembers = default;
            return false;
        }

        numberOfMembers = default;
        return true;
    }

    public bool EndArray()
    {
        _view.ConsumeWhitespace();

        return _view.Consume(']');
    }

    public bool BeginMember(out StringView name)
    {
        _view.ConsumeWhitespace();

        if (!_view.ExtractAndConsumeStringLiteral(out var sub))
        {
            name = default;
            return false;
        }

        name = new StringView(sub.String, sub.Index, sub.LastIndex);

        _view.ConsumeWhitespace();

        return _view.Consume(':');
    }

    public bool BeginMember(DeserializeToObjectDelegate deserialize, out object name)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractAndConsumeStringLiteral(out var sub))
        {
            _view.ConsumeWhitespace();

            if (_view.Consume(':'))
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
        _view.ConsumeWhitespace();

        if (_view.ExtractAndConsumeStringLiteral(out var sub))
        {
            _view.ConsumeWhitespace();

            if (_view.Consume(':'))
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
        _view.ConsumeWhitespace();

        if (this is not TReader reader)
        {
            name = default;
            return false;
        }

        if (deserialize(reader, out name))
        {
            _view.ConsumeWhitespace();

            if (_view.Consume(':'))
            {
                return true;
            }
        }

        name = default;
        return false;
    }

    public bool EndValue()
    {
        _view.ConsumeWhitespace();

        return _view.Consume(',');
    }

    public bool BeginObject()
    {
        _view.ConsumeWhitespace();

        return _view.Consume('{');
    }

    public bool EndObject()
    {
        _view.ConsumeWhitespace();

        return _view.Consume('}');
    }

    public bool Null()
    {
        _view.ConsumeWhitespace();

        return _view.Consume("null");
    }

    public bool Char(out char value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractStringLiteral(out var sub) && sub.Length == 1 && sub.First.HasValue)
        {
            _view.Consume(sub.Length);
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
        _view.ConsumeWhitespace();

        if (_view.ExtractAndConsumeStringLiteral(out var sub))
        {
            value = new StringView(sub.String, sub.Index, sub.LastIndex);
            return true;
        }

        value = default;
        return false;
    }

    public bool Boolean(out bool value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Float32(out float value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;

    }

    public bool Float64(out double value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Decimal(out decimal value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int8(out sbyte value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int16(out short value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int32(out int value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int64(out long value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt8(out byte value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt16(out ushort value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt32(out uint value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt64(out ulong value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractUntilAny(_endOfMemberSearchChars) is StringView sub)
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool DateTime(out DateTime value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractStringLiteral(out var sub))
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length + 2);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool TimeSpan(out TimeSpan value)
    {
        _view.ConsumeWhitespace();

        if (_view.ExtractStringLiteral(out var sub))
        {
            if (sub.TryParse(out value))
            {
                _view.Consume(sub.Length + 2);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Read<T>([NotNullWhen(returnValue: true)] out T? value) where T : ISpanParsable<T>
    {
        _view.ConsumeWhitespace();

        StringView sub;

        if (_view.First == '"' && _view.ExtractStringLiteral(out sub) && sub.TryParse(out value))
        {
            _view.Consume(sub.Length + 2);
            return true;
        }
        else
        if (_view.TryExtractUntilAny(values: _endOfMemberSearchChars, slice: out sub) && sub.TryParse(out value))
        {
            _view.Consume(sub.Length);
            return true;
        }

        value = default;
        return false;
    }
}
