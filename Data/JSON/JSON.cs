using System;
using System.Linq;
using System.IO;
using System.Text;

namespace Poly.Data {
    public partial class JSON : KeyValueCollection<object> {
        public char KeySeperatorCharacter = '.';

        public new object this[string Key] {
            get {
                return Get<object>(Key.Split(KeySeperatorCharacter));
            }
            set {
                Set(Key.Split(KeySeperatorCharacter), value);
            }
        }

        public object this[params string[] Keys] {
            get {
                return Get<object>(Keys);
            }
            set {
                Set(Keys, value);
            }
        }

        static JSON() {
            Parsers = new KeyValueCollection<WrapperDelegate>();

            RegisterParser(new ParserDelegate<bool>(bool.TryParse));
            RegisterParser(new ParserDelegate<int>(int.TryParse));
            RegisterParser(new ParserDelegate<long>(long.TryParse));
            RegisterParser(new ParserDelegate<float>(float.TryParse));
            RegisterParser(new ParserDelegate<double>(double.TryParse));
        }

        public JSON() { }

        public JSON(string Json) {
            Parse(Json, this);
        }

        public JSON(params object[] KeyValuePairs) {
            for (int i = 0; i < KeyValuePairs.Length - 1; i += 2) {
                Set(KeyValuePairs[i].ToString(), KeyValuePairs[i + 1]);
            }
        }

        public bool IsEmpty {
            get {
                return Count == 0;
            }
        }

        public bool IsArray { get; set; }

        public void Add(object Object) {
            var KVP = Object as KeyValuePair;

            if (KVP != null) {
				Add (KVP.Key ?? Count.ToString (), KVP.Value);	
			} 
			else {
				Add (Count.ToString (), Object);
			}
        }

        public virtual void CopyTo(JSON Object, params string[] Keys) {
            foreach (var K in Keys){
                Object.Set(K, Get(K));
            }
        }

        public virtual void CopyTo(JSON Object) {
            ForEach((k, v) => Object.AssignValue(k, v));
        }

        public JSON Sort() {
            var Keys = this.Keys.ToList();

            if (Keys.Count > 0)
                Keys.Sort();

            var Out = new JSON();
            foreach (var K in Keys) {
                Out.AssignValue(K, base[K]);
            }
            return Out;
        }

        public string ToPostString() {
            StringBuilder Output = new StringBuilder();

            ForEach((K, V) => {
				if (K != null && V != null)
                Output.AppendFormat("{0}={1}&", PostEncode(K), PostEncode(V.ToString()));
            });

            return Output.ToString(0, Output.Length - 1);
        }

        public string ToHtmlString() {
            return ToString(true).Replace("\t", "&emsp;")
                                  .Replace("\r\n", "<br/>");
        }

        public override string ToString() {
            return Stringify(this, false);
        }

        public virtual string ToString(bool HumanFormat) {
            return Stringify(this, HumanFormat);
        }

        public static string Stringify(JSON This, bool HumanFormat) {
            var Output = new StringBuilder();
            Stringify(This, Output, HumanFormat ? 0 : -1);
            return Output.ToString();
        }

        public static void Stringify(JSON This, StringBuilder Output = null, int Tabs = -1) {
            if (This == null) return;
            if (Output == null) Output = new StringBuilder();

            var IsArray = This.IsArray;
            int Index = 0, LastIndex = This.Count - 1;

            if (IsArray) Output.Append('[');
            else Output.Append('{');
                
            if (Tabs >= 0) Output.Append(App.NewLine).Append('\t', Tabs + 1);

            foreach (var Pair in This) {
                if (!IsArray) Output.Append('"').Append(Pair.Key).Append("\":");

                if (Pair.Value == null) Output.Append("null");
                else {
                    var Next = Pair.Value as JSON;
                    if (Next != null) Stringify(Next, Output, Tabs >= 0 ? Tabs + 1 : -1);

                    else {
                        var Str = Pair.Value as string;
                        if (Str != null) Output.Append('"').Append(Str.Escape()).Append('"');

                        else {
                            var Bool = Pair.Value as bool?;
                            if (Bool != null) Output.Append(Bool == true ? "true" : "false");
                            
                            else {
                                Output.Append(Pair.Value);
                            }
                        }
                    }
                }

                if (Index++ != LastIndex) {
                    Output.Append(',');
                    
                    if (Tabs >= 0) Output.Append(App.NewLine).Append('\t', Tabs + 1);
                }
                else {
                    if (Tabs >= 0) {
                        Output.Append(App.NewLine).Append('\t', Tabs);
                    }
                }
            }

            if (IsArray) Output.Append(']');
            else Output.Append('}');
        }

        public static string PostEncode(string Input) {
            int Len = 0;

            foreach (char c in Input) {
                if (!char.IsLetterOrDigit(c) && c != ' ') {
                    Len += 3;
                }
                else Len++;
            }

            if (Input.Length == Len)
                return Input.Replace(' ', '+');

            char[] Buffer = new char[Len];

            for (int i = 0, offset = 0; offset < Len && i < Input.Length; i++, offset++) {
                char c = Input[i];

                if (char.IsLetterOrDigit(c))
                    Buffer[offset] = c;
                else if (c == ' ')
                    Buffer[offset] = '+';
                else {
                    var str = c.ToHexString();

                    Buffer[offset] = '%';
                    Buffer[offset + 1] = str[0];
                    Buffer[offset + 2] = str[1];

                    offset += 2;
                }
            }

            return new string(Buffer);
        }

        public static JSON FromFile(string Path) {
            if (File.Exists(Path)) {
                return new JSON(File.ReadAllText(Path));
            }
            return new JSON();
        }

        public static void RegisterParser<T>(ParserDelegate<T> Handler) {
            Parsers.Add(typeof(T).Name, (str) => {
                T value;

                if (Handler(str, out value))
                    return value;

                return null;
            });
        }

        public static implicit operator JSON(string Text) {
            return Parse(Text);
        }

        public static JSON operator +(JSON Js, object Obj) {
            Js.Add(Obj);

            return Js;
        }

        public static JSON operator -(JSON Js, string Key) {
            Js[Key] = null;

            return Js;
        }
    }

    public class JSON<T> : JSON where T : class {
        public static Func<JSON<T>> NewTypedObject = () => { return new JSON<T>(); };

        public new T this[string Key] {
            get {
                return Get<T>(Key.Split(KeySeperatorCharacter));
            }
            set {
                Set(Key.Split(KeySeperatorCharacter), value);
            }
        }

        public new T this[params string[] Keys] {
            get {
                return Get<T>(Keys);
            }
            set {
                Set(Keys, value);
            }
        }

        public void Add(string Key, T Obj) {
            Set(Key, Obj);
        }
    }
}