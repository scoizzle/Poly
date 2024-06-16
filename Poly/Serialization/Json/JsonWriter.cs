using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Poly.Serialization
{
    public record struct JsonWriterOptions(
        bool PrettyPrint = false,
        int MaxDepth = 256
    );

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

            Text.Append(value ? "true" : "false");
            return true;
        }

        public bool Null()
        {
            if (!BeginValue())
                return false;

            Text.Append("null");
            return true;
        }

        public bool Char(char value)
        {
            if (!BeginValue())
                return false;

            Text.Append(value);
            return true;
        }

        public bool String(string value)
        {
            if (!BeginValue())
                return false;

            Text.AppendStringLiteral(value);
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

            Text.Append('"');

            AppendEscaped(Text, value);

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

        public bool Write<T>(T value) where T : ISpanFormattable
        {
            if (!BeginValue())
                return false;

            Span<char> chars = stackalloc char[512];

            if (!value.TryFormat(chars, out var charsWritten, default, default))
                return false;

            Text.Append(chars.Slice(charsWritten));
            return true;
        }

        private static readonly char[] CharsToEscape = [ReverseSolidus, QuotationMark];

        private static bool AppendEscaped(
            StringBuilder text,
            StringView view)
        {
            Span<char> hex = stackalloc char[4];

            while (view.ExtractUntilAny(CharsToEscape) is StringView sub)
            {
                text.Append(sub.AsSpan());

                var character = view.First;

                switch (view.First)
                {
                    case QuotationMark:
                        text.Append(ReverseSolidus).Append(QuotationMark);
                        break;

                    case ReverseSolidus:
                        text.Append(ReverseSolidus, 2);
                        break;

                    default:
                        GetHexString(character!.Value, hex);
                        text.Append(ReverseSolidus).Append('u').Append(hex);
                        break;
                }

                view.Consume(1);
            }

            text.Append(view.AsSpan());
            return true;
        }

        private static bool ShouldEscape(char value) =>
            value == QuotationMark ||
            value == ReverseSolidus ||
            value <= '\u001F';

        private static void GetHexString(
            char character,
            Span<char> hex)
        {
            ReadOnlySpan<byte> HexAlphabetBytes = "0123456789ABCDEF"u8;

            Span<char> slice = stackalloc char[1];
            Span<byte> bytes = stackalloc byte[2];

            slice[0] = character;

            Encoding.UTF8.GetBytes(slice, bytes);

            Span<char> result = hex[..4];

            result[0] = (char)HexAlphabetBytes[bytes[1] >> 4];
            result[1] = (char)HexAlphabetBytes[bytes[1] & 0xF];
            result[2] = (char)HexAlphabetBytes[bytes[0] >> 4];
            result[3] = (char)HexAlphabetBytes[bytes[0] & 0xF];
        }
    }
}
