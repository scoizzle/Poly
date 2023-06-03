using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Poly.Serialization
{
    public record struct JsonWriterOptions(
        bool PrettyPrint = false,
        int MaxDepth = 256
    );


    [DebuggerDisplay("Unavailable")]
    public class JsonWriterPipelines : IDataWriter
    {
        const char QuotationMark = '"';
        const char ReverseSolidus = '\\';

        int _depth;
        bool _hasPreviousMember;

        int _currentBufferSize;
        Memory<byte> _currentBuffer = Memory<byte>.Empty;

        readonly Pipe _pipe = new();

        public override string ToString()
        {
            FlushBuffer();

            var writer = _pipe.Writer;

            if (writer.UnflushedBytes > 0)
            {
                var flush = writer.FlushAsync();
                
                var result = flush.IsCompleted
                    ? flush.Result
                    : flush.AsTask().GetAwaiter().GetResult();

                if (result.IsCanceled)
                    return default;
            }

            {
                var reader = _pipe.Reader;

                var read = reader.ReadAsync();

                var result = read.IsCompleted
                    ? read.Result
                    : read.AsTask().GetAwaiter().GetResult();

                var buffer = result.Buffer;

                return string.Create((int)buffer.Length, buffer, static (buf, seq) => {
                    var numberOfCharactersWritten = 0;

                    while (!seq.IsEmpty)
                    {
                        var bytes = seq.FirstSpan;

                        var numberOfCharacters = Encoding.UTF8.GetCharCount(bytes);

                        var numberOfBytesConsumed = Encoding.UTF8.GetChars(bytes, buf[numberOfCharactersWritten..numberOfCharacters]);

                        seq = seq.Slice(numberOfBytesConsumed);

                        numberOfCharactersWritten += numberOfCharacters;
                    }
                });
            }
        }

        private Span<byte> GetWriteableSpan(int size)
        {
            if (_currentBuffer.Length < _currentBufferSize + size)
            {
                FlushBuffer();
                AcquireNewBuffer(size);
            }

            return _currentBuffer.Span[_currentBufferSize..];
        }

        private void FlushBuffer()
        {
            var writer = _pipe.Writer;

            if (_currentBufferSize > 0)
            {
                writer.Advance(_currentBufferSize);
                _currentBufferSize = 0;
            }
        }

        private void AcquireNewBuffer(int size)
        {
            _currentBuffer = _pipe.Writer.GetMemory(size);
        }

        private bool WriteChar(char character)
        {
            var writeable = GetWriteableSpan(1);

            writeable[0] = (byte)character;           

            _currentBufferSize += 1; 
            return true;
        }

        private bool WriteText(ReadOnlySpan<char> text)
        {
            var numberOfBytes = Encoding.UTF8.GetByteCount(text);

            var writeable = GetWriteableSpan(numberOfBytes);

            var numberOfBytesWritten = Encoding.UTF8.GetBytes(text, writeable);

            _currentBufferSize += numberOfBytesWritten;

            return numberOfBytes == numberOfBytesWritten;
        }

        private bool WriteStringLiteral(ReadOnlySpan<char> text)
        {
            var numberOfBytes = Encoding.UTF8.GetByteCount(text) + 2;

            var writeable = GetWriteableSpan(numberOfBytes);

            writeable[0] = (byte)QuotationMark;

            var numberOfBytesWritten = Encoding.UTF8.GetBytes(text, writeable[1..]) + 2;

            writeable[numberOfBytesWritten - 1] = (byte)QuotationMark;

            _currentBufferSize += numberOfBytesWritten;

            return numberOfBytes == numberOfBytesWritten;
        }
        
        public bool BeginValue()
        {
            if (_hasPreviousMember) {
                return WriteChar(',');
            }

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

            return WriteChar('{');
        }

        public bool EndObject()
        {
            _depth--;
        
            return WriteChar('}')
                && EndValue();
        }
        
        public bool BeginArray()
        {
            if (!BeginValue())
                return false;

            _depth++;
            _hasPreviousMember = false;

            return WriteChar('[');
        }

        public bool EndArray()
        {
            _depth--;
            
            return WriteChar(']')
                && EndValue();
        }

        public bool BeginMember(ReadOnlySpan<char> name) {
            if (!BeginValue())
                return false;
                
            _hasPreviousMember = false;

            return WriteStringLiteral(name)
                && WriteChar(':');
        }

        public bool BeginMember<T>(SerializeDelegate<T> serialize, in T name)
        {
            if (!BeginValue())
                return false;

            _hasPreviousMember = false;
                
            if (name is string str)
            {
                return WriteStringLiteral(str)
                    && WriteChar(':');
            }

            return WriteChar('"') &&
                   serialize(this, name) &&
                   WriteText("\":");
        }

        public bool BeginMember<TWriter, T>(SerializeDelegate<TWriter, T> serialize, in T name) where TWriter : class, IDataWriter
        {
            if (!BeginValue())
                return false;

            _hasPreviousMember = false;
                
            if (name is string str)
            {
                return WriteStringLiteral(str)
                    && WriteChar(':');
            }

            var writer = this as TWriter;
            Guard.IsNotNull(writer);

            return WriteChar('"') &&
                   serialize(writer, name) &&
                   WriteText("\":");
        }

        public bool Boolean(bool value)
        {
            return BeginValue() 
                && WriteText(value ? "true" : "false")
                && EndValue();
        }

        public bool Null()
        {
            return BeginValue() 
                && WriteText("null")
                && EndValue();
        }

        public bool Char(char value)
        {
            return BeginValue() 
                && WriteChar(value)
                && EndValue();
        }

        public bool DateTime(in DateTime value)
        {
            if (!BeginValue())
                return false;

            Span<char> chars = stackalloc char[32];

            chars[0] = '"';

            if (!value.TryFormat(chars[1..], out var numberOfCharacters))
                return false;

            chars[numberOfCharacters + 1] = '"';

            return WriteText(chars[..(numberOfCharacters + 2)])
                && EndValue();
        }

        public bool TimeSpan(in TimeSpan value)
        {
            if (!BeginValue())
                return false;

            Span<char> chars = stackalloc char[32];

            chars[0] = '"';

            if (!value.TryFormat(chars[1..], out var numberOfCharacters))
                return false;

            chars[numberOfCharacters + 1] = '"';

            return WriteText(chars[..(numberOfCharacters + 2)])
                && EndValue();
        }

        public bool String(in ReadOnlySpan<char> value)
        {
            return WriteStringLiteral(value)
                && EndValue();
        }

        const string NumberFormat = "G";
        public bool Number<T>(in T value) where T : INumber<T>
        {
            if (!BeginValue())
                return false;

            Span<char> chars = stackalloc char[32];

            if (!value.TryFormat(chars, out var numberOfCharacters, NumberFormat, default))
                return false;

            return WriteText(chars[..numberOfCharacters])
                && EndValue();
        }

        private static bool AppendEscaped(
            in StringBuilder text,
            in ReadOnlySpan<char> value)
        {
            Span<char> hex = stackalloc char[4];

            var remainingValue = value;

            while (SliceUntilCharacterToEscape(in remainingValue, out var slice, out var character))
            {
                text.Append(slice);

                switch (character) 
                {
                    case QuotationMark:
                        text.Append(ReverseSolidus).Append(QuotationMark);
                        break;

                    case ReverseSolidus:
                        text.Append(ReverseSolidus, 2);
                        break;

                    default:
                        GetHexString(character, hex);
                        text.Append(ReverseSolidus).Append('u').Append(hex);
                        break;
                }

                remainingValue = remainingValue[(slice.Length + 1)..];
            }

            text.Append(remainingValue);
            return true;
        }

        private static bool SliceUntilCharacterToEscape(
            in  ReadOnlySpan<char> value,
            out ReadOnlySpan<char> slice,
            out char               character)
        {
            var index = 0;

            while (index < value.Length)
            {
                character = value[index];

                if (ShouldEscape(character)) {
                    slice = value[..index];
                    return true;
                }

                index++;
            }

            character = default;
            slice = value;
            return false;
        }

        private static bool ShouldEscape(char value) => 
            value == QuotationMark ||
            value == ReverseSolidus ||
            value <= '\u001F';

        private static void GetHexString(
            char character,
            Span<char> hex)
        {
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

        private static ReadOnlySpan<byte> HexAlphabetBytes => "0123456789ABCDEF"u8;
    }

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

        public bool BeginMember(ReadOnlySpan<char> name) {
            if (!String(in name))
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

        public bool String(in ReadOnlySpan<char> value)
        {
            if (!BeginValue())
                return false;
            
            Text.Append('"');

            AppendEscaped(Text, in value);

            Text.Append('"');
            return true;
        }

        public bool Number<T>(in T value) where T : INumber<T>
        {
            if (!BeginValue())
                return false;
                
            Text.Append(value);
            return true;
        }

        private static bool AppendEscaped(
            in StringBuilder text,
            in ReadOnlySpan<char> value)
        {
            Span<char> hex = stackalloc char[4];

            var remainingValue = value;

            while (SliceUntilCharacterToEscape(in remainingValue, out var slice, out var character))
            {
                text.Append(slice);

                switch (character) 
                {
                    case QuotationMark:
                        text.Append(ReverseSolidus).Append(QuotationMark);
                        break;

                    case ReverseSolidus:
                        text.Append(ReverseSolidus, 2);
                        break;

                    default:
                        GetHexString(character, hex);
                        text.Append(ReverseSolidus).Append('u').Append(hex);
                        break;
                }

                remainingValue = remainingValue[(slice.Length + 1)..];
            }

            text.Append(remainingValue);
            return true;
        }

        private static bool SliceUntilCharacterToEscape(
            in  ReadOnlySpan<char> value,
            out ReadOnlySpan<char> slice,
            out char               character)
        {
            var index = 0;

            while (index < value.Length)
            {
                character = value[index];

                if (ShouldEscape(character)) {
                    slice = value[..index];
                    return true;
                }

                index++;
            }

            character = default;
            slice = value;
            return false;
        }

        private static bool ShouldEscape(char value) => 
            value == QuotationMark ||
            value == ReverseSolidus ||
            value <= '\u001F';

        private static void GetHexString(
            char character,
            Span<char> hex)
        {
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

        private static ReadOnlySpan<byte> HexAlphabetBytes => "0123456789ABCDEF"u8;
    }
}
