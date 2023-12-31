namespace Poly.Serialization;

using Poly.Parsing;
using Poly.Parsing.Json;

public record JsonReaderPipelines(in ReadOnlySequence<char> Sequence) : IDataReader
{
    readonly Grammar<char, JsonToken>.TokenParser _parser = GetParser(Sequence);

    public bool IsDone => _parser.IsDone;

    public bool BeginArray(out int? numberOfMembers)
    {
        numberOfMembers = default;

        if (_parser.Current.Token != JsonToken.BeginArray)
            return false;

        _parser.MoveNext();
        return true;
    }

    public bool BeginMember(out StringView name)
    {
        name = default;

        if (_parser.Current.Token != JsonToken.String)
            return false;

        var nameSegment = _parser.Current.Segment;
        
        if (!_parser.MoveNext())
            return false;

        if (_parser.Current.Token != JsonToken.NameSeperator)
            return false;

        name = nameSegment.ToString();

        _parser.MoveNext();
        return true;
    }

    public bool BeginMember<T>(DeserializeDelegate<T> deserializer, out T name)
    {
        if (deserializer(this, out name)) {
            _parser.MoveNext();
            return true;
        }

        name = default;
        return false;
    }

    public bool BeginMember<TReader, T>(DeserializeDelegate<TReader, T> deserialize, [NotNullWhen(true)] out T? name) where TReader : class, IDataReader
    {
        name = default;

        if (_parser.Current.Token != JsonToken.String)
            return false;

        if (this is not TReader reader) return false;

        if (!deserialize(reader, out name))
            return false;
        
        if (!_parser.MoveNext())
            return false;

        if (_parser.Current.Token != JsonToken.NameSeperator)
            return false;
            
        _parser.MoveNext();
        return true;
    }

    public bool BeginObject()
    {
        if (_parser.Current.Token != JsonToken.BeginObject)
            return false;

        _parser.MoveNext();
        return true;
    }

    public bool Boolean(out bool value)
    {
        if (_parser.Current.Token == JsonToken.TrueValue) {
            value = true;

            _parser.MoveNext();
            return true;
        }

        if (_parser.Current.Token == JsonToken.FalseValue) {
            value = false;

            _parser.MoveNext();
            return true;
        }
        
        value = false;
        return false;
    }

    public bool Char(out char value)
    {
        if (_parser.Current.Token != JsonToken.String)
        {
            value = default;
            return false;
        }

        var segment = _parser.Current.Segment;

        if (segment.Length == 0)
        {
            value = default;
            return false;
        }

        var firstCharacter = segment.FirstSpan[0];

        if (segment.Length == 1)
        {
            value = firstCharacter;
            return true;
        }

        if (firstCharacter != '\\') 
        {
            value = default;
            return false;
        }

        value = segment.FirstSpan[1];

        switch (value)
        {
            case '"':
            case '\\':
            case '/':
            case 'b':
            case 'f':
            case 'n':
            case 'r':
            case 't':
                return true;

            case 'u':
            {
                var reader = new SequenceReader<char>(segment.Slice(2));

                Span<char> hex = stackalloc char[4];

                for (var i = 0; i < 4; i++)
                {
                    if (!reader.TryRead(out hex[i]))
                        return false;
                }

                Span<byte> bytes = stackalloc byte[2];

                bytes[1] = (byte)(hex[0] << 4 | hex[1] & 0xF);
                bytes[0] = (byte)(hex[2] << 4 | hex[3] & 0xF);

                var characterCount = Encoding.UTF8.GetChars(bytes, hex);

                if (characterCount != 1)
                    return false;

                value = hex[0];
                return true;
            }

            default:
                return false;
        }
    }

    public bool DateTime(out DateTime value)
    {
        if (_parser.Current.Token != JsonToken.String)
        {
            value = default;
            return false;
        }

        if (!System.DateTime.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool Decimal(out decimal value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!decimal.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool EndArray()
    {
        if (_parser.Current.Token != JsonToken.EndArray)
            return false;

        _parser.MoveNext();
        return true;
    }

    public bool EndObject()
    {
        if (_parser.Current.Token != JsonToken.EndObject)
            return false;

        _parser.MoveNext();
        return true;
    }

    public bool EndValue()
    {
        if (_parser.Current.Token == JsonToken.MemberSeperator)
        {
            _parser.MoveNext();
            return true;
        }

        return false;
    }

    public bool Float32(out float value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!float.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool Float64(out double value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!double.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool Int16(out short value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!short.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool Int32(out int value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!int.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool Int64(out long value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!long.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool Int8(out sbyte value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!sbyte.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool Null()
    {
        if (_parser.Current.Token != JsonToken.NullValue)
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool String(out string value)
    {
        if (_parser.Current.Token != JsonToken.String)
        {
            value = default;
            return false;
        }

        value = _parser.Current.Segment.ToString();

        _parser.MoveNext();
        return true;
    }

    public bool StringView(out StringView value)
    {
        if (_parser.Current.Token != JsonToken.String)
        {
            value = default;
            return false;
        }

        value = _parser.Current.Segment.ToString();

        _parser.MoveNext();
        return true;
    }

    public bool TimeSpan(out TimeSpan value)
    {
        if (_parser.Current.Token != JsonToken.String)
        {
            value = default;
            return false;
        }

        if (!System.TimeSpan.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool UInt16(out ushort value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!ushort.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool UInt32(out uint value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!uint.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool UInt64(out ulong value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!ulong.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    public bool UInt8(out byte value)
    {
        if (_parser.Current.Token != JsonToken.Number)
        {
            value = default;
            return false;
        }

        if (!byte.TryParse(_parser.Current.Segment.FirstSpan, out value))
        {
            return false;
        }

        _parser.MoveNext();
        return true;
    }

    static Grammar<char, JsonToken>.TokenParser GetParser(in ReadOnlySequence<char> sequence)
    {
        var parser = JsonGrammar.Definition.ParseAllTokens(in sequence);

        parser.MoveNext();

        return parser;
    }
}

public sealed class JsonReader : IDataReader
{
    StringView _stringView;

    public JsonReader(string text) 
        : this (new StringView(text)) 
    { }

    public JsonReader(StringView view)
    {
        _stringView = view;
    }

    public bool IsDone => _stringView.IsDone;

    public bool BeginArray(out int? numberOfMembers)
    {
        _stringView.ConsumeWhitespace();

        if (!_stringView.Consume('['))
        {
            numberOfMembers = default;
            return false;
        }

        var entryPoint = _stringView.Index;

        var count = 0;
        var depth = 1;

        while (!_stringView.IsDone) {
            _stringView.ConsumeWhitespace();

            if (_stringView.Consume(']') && --depth == 0)
                break;

            var parsedSomething = false;

            switch (_stringView.Current) {
                case 't':
                case 'f':
                    parsedSomething = _stringView.Extract(out bool _) && _stringView.ConsumeWhitespace();
                    break;

                case 'n':
                    parsedSomething = _stringView.Consume("null") && _stringView.ConsumeWhitespace();
                    break;

                case '"':
                    parsedSomething = _stringView.ExtractStringLiteral(out _, true) && _stringView.ConsumeWhitespace();
                    break;

                case '[':
                    depth++;
                    parsedSomething = _stringView.Consume() && _stringView.ConsumeWhitespace();
                    break;

                case '{':
                    var closingCurlyBrace = _stringView.FindMatchingBracket('{', '}');

                    if (closingCurlyBrace == -1)
                        break;

                    _stringView.Index = closingCurlyBrace + 1;
                    _stringView.ConsumeWhitespace();

                    parsedSomething = true;
                    break;
            }

            if (!_stringView.Consume(',')) {
                _stringView.ConsumeWhitespace();

                if (_stringView.Current != ']') {
                    numberOfMembers = default;
                    return false;
                }
            }

            if (parsedSomething) {
                count++;
                continue;
            }
        }

        _stringView.Index = entryPoint;
        numberOfMembers = count;
        return true;
    }
    
    public bool EndArray()
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Consume(']');
    }

    public bool BeginMember(out StringView name)
    {
        _stringView.ConsumeWhitespace();

        if (!_stringView.ExtractStringLiteral(out name))
            return false;

        _stringView.ConsumeWhitespace();
        
        return _stringView.Consume(':');
    }

    public bool BeginMember(DeserializeObjectDelegate deserialize, out object name)
    {
        _stringView.ConsumeWhitespace();

        if (_stringView.ExtractStringLiteral(out var sub)) {
            _stringView.ConsumeWhitespace();

            if (_stringView.Consume(':')) {
                return deserialize(new StringReader(sub), out name);
            }
        }
        
        name = default;
        return false;
    }

    public bool BeginMember<T>(DeserializeDelegate<T> deserialize, out T name)
    {
        _stringView.ConsumeWhitespace();

        if (_stringView.ExtractStringLiteral(out var sub)) {
            _stringView.ConsumeWhitespace();

            if (_stringView.Consume(':')) {
                return deserialize(new StringReader(sub), out name);
            }
        }
        
        name = default;
        return false;
    }

    public bool BeginMember<TReader, T>(DeserializeDelegate<TReader, T> deserialize, [NotNullWhen(true)] out T? name) where TReader : class, IDataReader
    {
        _stringView.ConsumeWhitespace();

        if (this is not TReader reader)
        {
            name = default;
            return false;
        }

        if (deserialize(reader, out name))
        {
            _stringView.ConsumeWhitespace();

            if (_stringView.Consume(':')) {
                return true;
            }
        }
        
        name = default;
        return false;
    }

    public bool EndValue()
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Consume(',');
    }

    public bool BeginObject()
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Consume('{');
    }

    public bool EndObject()
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Consume('}');
    }

    public bool Null()
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Consume("null");
    }

    public bool Char(out char value)
    {
        _stringView.ConsumeWhitespace();

        if (_stringView.ExtractStringLiteral(out var view) && view.Length == 1) {
            value = view.Current;
            return true;
        }

        value = default;
        return false;
    }

    public bool String(out string value)
    {
        if (StringView(out var view)) {
            value = view.ToString();
            return true;
        }

        value = default;
        return Null();
    }

    public bool StringView(out StringView value)
    {
        _stringView.ConsumeWhitespace();

        if (_stringView.ExtractStringLiteral(out value)) {
            return true;
        }

        value = default;
        return false;
    }

    public bool Boolean(out bool value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool Float32(out float value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);

    }

    public bool Float64(out double value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }
    
    public bool Decimal(out decimal value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool Int8(out sbyte value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool Int16(out short value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool Int32(out int value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool Int64(out long value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool UInt8(out byte value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool UInt16(out ushort value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool UInt32(out uint value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool UInt64(out ulong value)
    {
        _stringView.ConsumeWhitespace();

        return _stringView.Extract(out value);
    }

    public bool DateTime(out DateTime value)
    {
        if (StringView(out var view)) {
            return System.DateTime.TryParse(view.AsSpan(), out value);
        }

        value = default;
        return false;
    }

    public bool TimeSpan(out TimeSpan value)
    {
        if (StringView(out var view)) {
            return System.TimeSpan.TryParse(view.AsSpan(), out value);
        }

        value = default;
        return false;
    }
}


public sealed class JsonReaderSlice : IDataReader
{
    private static readonly char[] EndOfMemberSearchChars = { ',', '}', ']' };

    StringSlice slice;

    public JsonReaderSlice(StringSlice view)
    {
        slice = view;
    }

    public bool IsDone => !slice.IsEmpty;

    public bool BeginArray(out int? numberOfMembers)
    {
        slice.Consume(char.IsWhiteSpace);

        if (!slice.Consume('['))
        {
            numberOfMembers = default;
            return false;
        }

        numberOfMembers = default;
        return true;
    }
    
    public bool EndArray()
    {
        slice.Consume(char.IsWhiteSpace);

        return slice.Consume(']');
    }

    public bool BeginMember(out StringView name)
    {
        slice.Consume(char.IsWhiteSpace);

        if (!slice.ExtractStringLiteral(out var sub)){
            name = default;
            return false;
        }

        name = new StringView(sub.String, sub.Begin, sub.End);

        slice.Consume(char.IsWhiteSpace);
        
        return slice.Consume(':');
    }

    public bool BeginMember(DeserializeObjectDelegate deserialize, out object name)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice.ExtractStringLiteral(out var sub)) {
            slice.Consume(char.IsWhiteSpace);

            if (slice.Consume(':')) {
                var view = new StringView(sub.String, sub.Begin, sub.End);

                return deserialize(new StringReader(view), out name);
            }
        }
        
        name = default;
        return false;
    }

    public bool BeginMember<T>(DeserializeDelegate<T> deserialize, out T name)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice.ExtractStringLiteral(out var sub)) {
            slice.Consume(char.IsWhiteSpace);

            if (slice.Consume(':')) {
                var view = new StringView(sub.String, sub.Begin, sub.End);

                return deserialize(new StringReader(view), out name);
            }
        }
        
        name = default;
        return false;
    }

    public bool BeginMember<TReader, T>(DeserializeDelegate<TReader, T> deserialize, [NotNullWhen(true)] out T? name) where TReader : class, IDataReader
    {
        slice.Consume(char.IsWhiteSpace);

        if (this is not TReader reader)
        {
            name = default;
            return false;
        }

        if (deserialize(reader, out name))
        {
            slice.Consume(char.IsWhiteSpace);

            if (slice.Consume(':')) {
                return true;
            }
        }
        
        name = default;
        return false;
    }

    public bool EndValue()
    {
        slice.Consume(char.IsWhiteSpace);

        return slice.Consume(',');
    }

    public bool BeginObject()
    {
        slice.Consume(char.IsWhiteSpace);

        return slice.Consume('{');
    }

    public bool EndObject()
    {
        slice.Consume(char.IsWhiteSpace);

        return slice.Consume('}');
    }

    public bool Null()
    {
        slice.Consume(char.IsWhiteSpace);

        return slice.Consume("null");
    }

    public bool Char(out char value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice.ExtractStringLiteral(out var view) && view.Length == 1 && view.Current.HasValue) {
            value = view.Current.Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool String(out string value)
    {
        if (StringView(out var view)) {
            value = view.ToString();
            return true;
        }

        value = default;
        return Null();
    }

    public bool StringView(out StringView value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice.ExtractStringLiteral(out var sub)) {
            value = new StringView(sub.String, sub.Begin, sub.End);
            return true;
        }

        value = default;
        return false;
    }

    public bool Boolean(out bool value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Float32(out float value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;

    }

    public bool Float64(out double value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }
    
    public bool Decimal(out decimal value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int8(out sbyte value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int16(out short value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int32(out int value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Int64(out long value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt8(out byte value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt16(out ushort value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt32(out uint value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool UInt64(out ulong value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice[EndOfMemberSearchChars] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool DateTime(out DateTime value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice['"', '"'] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length + 2);
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool TimeSpan(out TimeSpan value)
    {
        slice.Consume(char.IsWhiteSpace);

        if (slice['"', '"'] is StringSlice sub) {
            if (sub.TryParse(out value)) {
                slice.Consume(sub.Length + 2);
                return true;
            }
        }

        value = default;
        return false;
    }
}
