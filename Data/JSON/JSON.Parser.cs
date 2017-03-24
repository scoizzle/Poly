using System;

namespace Poly.Data {
    public partial class JSON {
        public static JSON Parse(string Text) {
            return Parse(Text, 0, Text.Length);
        }

        public static JSON Parse(string Text, int Index) {
            return Parse(Text, Index, Text.Length);
        }

        public static JSON Parse(string Text, int Index, int Length) {
            var It = new StringIterator(Text, Index, Length);
            object Result;

            if ((Result = _Object(It)) != null ||
                (Result = _Array(It)) != null)
                return Result as JSON;

            return null;
        }

        public static bool Parse(string Text, JSON Storage) {
            return Parse(Text, 0, Text.Length, Storage);
        }

        public static bool Parse(string Text, int Index, JSON Storage) {
            return Parse(Text, Index, Text.Length, Storage);
        }

        public static bool Parse(string Text, int Index, int Length, JSON Storage) {
            var It = new StringIterator(Text, Index, Length);

            if (_Object(It, Storage) != null)
                return true;
            else
            if (_Array(It, Storage) != null)
                return true;
            else
                return false;
        }

        private static Func<char, bool>[] WhitespaceFuncs = new Func<char, bool>[] {
            char.IsWhiteSpace, c => c == ','
        };

        private static JSON _Object(StringIterator It) {
            return _Object(It, new JSON());
        }
            
        private static JSON _Object(StringIterator It, JSON Storage) {
            if (It.Consume('{')) {
                It.Consume(WhitespaceFuncs);
                var Start = It.Index;

                if (It.Goto('{', '}')) {
                    var Data = It.Clone(Start, It.Index);

                    while (!Data.IsDone()) {
                        Data.Consume(WhitespaceFuncs);

                        var Pair = _KeyValuePair(Data);

                        if (Pair == null)
                            return null;

                        Storage.Set(Pair);
                        Data.Consume(WhitespaceFuncs);
                    }

                    It.Consume('}');
                    return Storage;
                }
            }
            return null;
        }

        private static JSON _Array(StringIterator It) {
            return _Array(It, new JSON() { IsArray = true });
        }

        private static JSON _Array(StringIterator It, JSON Storage) {
            if (It.Consume('[')) {
                It.Consume(WhitespaceFuncs);
                var Start = It.Index;

                if (It.Goto('[', ']')) {
                    var Data = It.Clone(Start, It.Index);

                    while (!Data.IsDone()) {
                        Data.Consume(WhitespaceFuncs);

                        object Value;
                        if (_getValue(Data, out Value)) Storage.Add(Value);
                        else {
                            Log.Error("JSON Failed to parse ", Data);
                            return null;
                        }

                        Data.Consume(WhitespaceFuncs);
                    }

                    It.Consume(']');
                    Storage.IsArray = true;
                    return Storage;
                }
            }
            return null;
        }

        private static KeyValuePair _KeyValuePair(StringIterator It) {
            var Key = _String(It);

            if (Key == null)
                return null;

            It.ConsumeWhitespace();

            if (It.Consume(':')) {
                It.ConsumeWhitespace();
                object Value;

                if (_getValue(It, out Value))
                    return new KeyValuePair(Key, Value);
            }

            Log.Error("Failed to parse {0}", It);
            return null;
        }

        private static bool _getValue(StringIterator It, out object Value) {
            return (Value = _Object(It)) != null ||
                   (Value = _Array(It)) != null ||
                   (Value = _String(It)) != null ||
                   (Value = _Number(It)) != null ||
                   (Value = _Boolean(It)) != null ||
                   _IsNullLitteral(It);
        }

        private static string _String(StringIterator It) {
            if (It.IsAt('"')) {
                return It.Extract('"', '"');
            }
            else if (It.IsAt('\'')) {
                return It.Extract('\'', '\'');
            }
            return null;
        }

        private static object _Number(StringIterator It) {
            var Begin = It.Index;

            It.Consume("-");

            if (It.Consume(char.IsDigit))
                if (It.Consume('.'))
                    It.Consume(char.IsDigit);

            if (It.Consume('e') || It.Consume('E')) {
                if (!It.Consume('-'))
                    It.Consume('+');

                It.Consume(char.IsDigit);
            }

            if (It.Index > Begin) {
                object Result;
                string Sub = It.Substring(Begin, It.Index - Begin);
                
                foreach (var Pair in Parsers) {
                    Result = Pair.Value(Sub);

                    if (Result != null)
                        return Result;
                }
            }

            It.Index = Begin;
            return null;
        }

        private static object _Boolean(StringIterator It) {
            if (It.Consume("true"))
                return true;
            else if (It.Consume("false"))
                return false;

            return null;
        }

        private static bool _IsNullLitteral(StringIterator It) {
            return It.Consume("null");
        }
    }
}
