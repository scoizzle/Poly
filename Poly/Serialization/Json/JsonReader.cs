using System;

namespace Poly.Serialization {
    public class JsonReader : ReaderInterface
    {
        StringView _stringView;

        public JsonReader(string text) {
            _stringView = new StringView(text);
        }

        public JsonReader(StringView view) {
            _stringView = view;
        }

        public bool IsDone => _stringView.IsDone;

        public bool BeginArray()
        {
            _stringView.ConsumeWhitespace();

            return _stringView.Consume('[');
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

        public bool BeginMember(DeserializeDelegate deserialize, out object name)
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
            return false;
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
}