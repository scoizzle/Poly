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
			var js = new JSON();

			if (Parse(Text, Index, Length, js))
				return js;

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
			
            if (_Array(It, Storage) != null)
                return true;
			
            return false;
        }

		static Func<char, bool>[] WhitepsaceFuncs = {
			char.IsWhiteSpace, c => c == ','
		};

		static bool _getValue(StringIterator It, out object Value) {
			return (Value = _Object(It)) != null ||
				   (Value = _Array(It)) != null ||
				   (Value = _String(It)) != null ||
				   (Value = _Boolean(It)) != null ||
				   (Value = _Number(It)) != null ||
				   _NullLitteral(It);
		}

		static JSON _Object(StringIterator It, JSON Storage = null) {
			if (It.SelectSection('{', '}')) {
				if (Storage == null) Storage = new JSON();
				It.ConsumeWhitespace();

				while (!It.IsDone()) {
					var Pair = _KeyValuePair(It);

					if (Pair == null) return null;

					Storage.Set(Pair);

					if (!It.Consume(WhitepsaceFuncs))
						break;
				}

				It.ConsumeSection();
				return Storage;
			}

			return null;
        }

		static JSON _Array(StringIterator It, JSON Storage = null) {
			if (It.SelectSection('[', ']')) {
				if (Storage == null) Storage = new JSON();
				It.ConsumeWhitespace();

				while (!It.IsDone()) {
					object Value;
					if (_getValue(It, out Value)) Storage.Add(Value);
					else {
						Log.Error("JSON Failed to parse ", It);
						return null;
					}

					if (!It.Consume(WhitepsaceFuncs))
						break;
				}

				It.ConsumeSection();
				Storage.IsArray = true;
				return Storage;
			}

			return null;
        }

        static KeyValuePair _KeyValuePair(StringIterator It) {
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

        static object _Number(StringIterator It) {
			var Start = It.Index;
			bool isFloatPoint = false;

			It.Consume("-");

			if (It.Consume(char.IsDigit))
				if (It.Consume('.')) {
					isFloatPoint = true;
					It.Consume(char.IsDigit);
				}

			if (It.Consume('e') || It.Consume('E')) {
				if (!It.Consume('-'))
					It.Consume('+');

				It.Consume(char.IsDigit);
			}

			var str = It.Substring(Start, It.Index - Start);

			if (isFloatPoint) {
				float flt;
				double dbl;

				if (float.TryParse(str, out flt))
					return flt;

				if (double.TryParse(str, out dbl))
					return dbl;
			}
			else {
				int n;
				long lng;

				if (int.TryParse(str, out n))
					return n;

				if (long.TryParse(str, out lng))
					return lng;
			}

			return str;
		}

		static string _String(StringIterator It) {
			return It.Extract('"', '"') ?? It.Extract('\'', '\'');
		}

        static bool? _Boolean(StringIterator It) {
			if (It.Consume("true")) return true;
			if (It.Consume("false")) return false;
			return null;
        }

        static bool _NullLitteral(StringIterator It) {
            return It.Consume("null");
        }
    }
}
