using Microsoft.Extensions.Primitives;
using Poly.Net.Http;

namespace Poly.Serialization;

public class JsonWriter : IDataWriter
{
    const char QuotationMark = '"';
    const char ReverseSolidus = '\\';

    int _depth;
    bool _hasPreviousMember;

    public StringBuilder Text { get; init; } = new();

    public bool BeginValue()
    {
        if (_hasPreviousMember)
            Text.Append(',');

        return true;
    }

    public bool EndValue()
    {
        _hasPreviousMember = true;
        return true;
    }

    public bool BeginObject()
    {
        if (!BeginValue())
            return false;

        _depth++;
        _hasPreviousMember = false;

        Text.Append('{');
        return true;
    }

    public bool EndObject()
    {
        _depth--;

        Text.Append('}');
        return true;
    }

    public bool BeginArray()
    {
        if (!BeginValue())
            return false;

        _depth++;
        _hasPreviousMember = false;

        Text.Append('[');
        return true;
    }

    public bool EndArray()
    {
        _depth--;

        Text.Append(']');
        return true;
    }

    public bool BeginMember(StringView name)
    {
        if (!StringView(name))
            return false;

        _hasPreviousMember = false;

        Text.Append(':');
        return true;
    }

    public bool BeginMember<T>(SerializeDelegate<T> serialize, in T name)
    {
        if (!BeginValue())
            return false;

        _hasPreviousMember = false;

        if (name is string str)
        {
            if (!String(str))
                return false;

            Text.Append(':');
            return true;
        }

        Text.Append('"');

        if (!serialize(this, name))
            return false;

        Text.Append("\":");
        return true;
    }

    public bool BeginMember<TWriter, T>(SerializeDelegate<TWriter, T> serialize, in T name) where TWriter : class, IDataWriter
    {
        if (!BeginValue())
            return false;

        _hasPreviousMember = false;

        if (name is string str)
        {
            if (!String(str))
                return false;

            Text.Append(':');
            return true;
        }

        Text.Append('"');

        var writer = this as TWriter;
        Guard.IsNotNull(writer);

        if (!serialize(writer, name))
            return false;

        Text.Append("\":");
        return true;
    }

    public bool Boolean(bool value)
    {
        if (!BeginValue())
            return false;

        var text = value ? "true" : "false";

        var sizeHint = Text.Length + text.Length;
        Text.EnsureCapacity(sizeHint);

        Text.Append(text);
        return true;
    }

    public bool Null()
    {
        if (!BeginValue())
            return false;

        var sizeHint = Text.Length + 4;
        Text.EnsureCapacity(sizeHint);

        Text.Append("null");
        return true;
    }

    public bool Char(char value)
    {
        if (!BeginValue())
            return false;

        var sizeHint = Text.Length + 3;
        Text.EnsureCapacity(sizeHint);

        Text.Append(value);
        return true;
    }

    public bool String(string value)
    {
        if (!BeginValue())
            return false;

        var sizeHint = Text.Length + value.Length + 2;
        Text.EnsureCapacity(sizeHint);

        Text.Append('"');
        Text.Append(value);
        Text.Append('"');
        return true;
    }

    public bool DateTime(in DateTime value)
    {
        if (!BeginValue())
            return false;

        Text.Append('"').Append(value).Append('"');
        return true;
    }

    public bool TimeSpan(in TimeSpan value)
    {
        if (!BeginValue())
            return false;

        Text.Append('"').Append(value).Append('"');
        return true;
    }

    public bool StringView(StringView value)
    {
        if (!BeginValue())
            return false;

        var sizeHint = Text.Length + value.Length + 2;
        Text.EnsureCapacity(sizeHint);

        Text.Append('"');
        Text.Append(value.AsSpan());
        Text.Append('"');
        return true;
    }

    public bool Number<T>(T value) where T : INumber<T>
    {
        if (!BeginValue())
            return false;

        Text.Append(value);
        return true;
    }

    public bool Write<T>(T value, ReadOnlySpan<char> format = default, IFormatProvider? formatProvider = default) where T : ISpanFormattable
    {
        if (!BeginValue())
            return false;

        Span<char> chars = stackalloc char[128];

        if (!value.TryFormat(chars, out var charsWritten, format, formatProvider))
            return false;

        var sizeHint = Text.Length + charsWritten;
        Text.EnsureCapacity(sizeHint);

        Span<char> slice = chars.Slice(0, charsWritten);
        Text.Append(slice);
        return true;
    }


    // private static readonly SearchValues<char> s_CharactersToEscape = SearchValues.Create("\x0\x1\x2\x3\x4\x5\x6\x7\x8\x9\xA\xB\xC\xD\xE\xF\x10\x11\x12\x13\x14\x15\x16\x17\x18\x19\x1A\x1B\x1C\x1D\x1E\x1F\x22\x5C\x7F\x80\x81\x82\x83\x84\x85\x86\x87\x88\x89\x8A\x8B\x8C\x8D\x8E\x8F\x90\x91\x92\x93\x94\x95\x96\x97\x98\x99\x9A\x9B\x9C\x9D\x9E\x9F");

    // private bool WriteEncoded(ReadOnlySpan<char> chars)
    // {
    //     int index = 0;
    //     Span<char> escapedSequence = stackalloc char[6];

    //     while (true)
    //     {
    //         index = chars.IndexOfAny(s_CharactersToEscape);

    //         if (index == -1)
    //         {
    //             m_Writer.Write(chars);
    //             return true;
    //         }

    //         if (index != 0)
    //         {
    //             var precedingChars = chars.Slice(0, index);
    //             m_Writer.Write(precedingChars);
    //         }

    //         var character = chars[index];

    //         switch (character)
    //         {
    //             case '\\':
    //                 m_Writer.Write("\\\\");
    //                 break;
    //             case '"':
    //                 m_Writer.Write("\\\"");
    //                 break;
    //             case '\r':
    //                 m_Writer.Write("\\r");
    //                 break;
    //             case '\n':
    //                 m_Writer.Write("\\n");
    //                 break;
    //             case '\t':
    //                 m_Writer.Write("\\t");
    //                 break;
    //             case '\b':
    //                 m_Writer.Write("\\b");
    //                 break;
    //             case '\f':
    //                 m_Writer.Write("\\f");
    //                 break;
    //             default:
    //                 GetUTF8EscapedCharacterSequence(character, escapedSequence);
    //                 m_Writer.Write(escapedSequence);
    //                 break;
    //         }

    //         chars = chars.Slice(index + 1);
    //     }
    // }

    // private static void GetUTF8EscapedCharacterSequence(
    //     char character,
    //     in Span<char> result)
    // {
    //     ReadOnlySpan<byte> HexAlphabetBytes = "0123456789abcdef"u8;

    //     ReadOnlySpan<char> charsToEncode = [character];
    //     ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(charsToEncode);

    //     result[0] = '\\';
    //     result[1] = 'u';
    //     result[2] = (char)HexAlphabetBytes[bytes[1] >> 4];
    //     result[3] = (char)HexAlphabetBytes[bytes[1] & 0xF];
    //     result[4] = (char)HexAlphabetBytes[bytes[0] >> 4];
    //     result[5] = (char)HexAlphabetBytes[bytes[0] & 0xF];
    // }

    private static void GetHexString(
        char character,
        Span<char> hex)
    {
        ReadOnlySpan<byte> hexAlphabetBytes = "0123456789ABCDEF"u8;

        Span<char> slice = stackalloc char[1];
        Span<byte> bytes = stackalloc byte[2];

        slice[0] = character;

        Encoding.UTF8.GetBytes(slice, bytes);

        Span<char> result = hex[..4];

        result[0] = (char)hexAlphabetBytes[bytes[1] >> 4];
        result[1] = (char)hexAlphabetBytes[bytes[1] & 0xF];
        result[2] = (char)hexAlphabetBytes[bytes[0] >> 4];
        result[3] = (char)hexAlphabetBytes[bytes[0] & 0xF];
    }

    public bool DateTime(DateTime value)
    {
        Text.Append('"');
        Text.Append(value);
        Text.Append('"');
        return true;
    }

    public bool TimeSpan(TimeSpan value)
    {
        Text.Append('"');
        Text.Append(value);
        Text.Append('"');
        return true;
    }

    public bool BeginMember<T>(SerializeDelegate<T> serializer, T name)
    {
        return serializer(this, name);
    }
}
