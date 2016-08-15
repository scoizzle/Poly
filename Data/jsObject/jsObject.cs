using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Dynamic;
using System.Threading.Tasks;

namespace Poly.Data {    
    public partial class jsObject : KeyValueCollection<object> {
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

        static jsObject() {
            Parsers = new KeyValueCollection<WrapperDelegate>();

            RegisterParser(new ParserDelegate<bool>(bool.TryParse));
            RegisterParser(new ParserDelegate<int>(int.TryParse));
            RegisterParser(new ParserDelegate<long>(long.TryParse));
            RegisterParser(new ParserDelegate<float>(float.TryParse));
            RegisterParser(new ParserDelegate<double>(double.TryParse));
            RegisterParser(new ParserDelegate<decimal>(decimal.TryParse));
        }

        public jsObject() { }

        public jsObject(string Json) {
            Parse(Json, this);
        }

        public jsObject(params object[] KeyValuePairs) {
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
			if (Object is KeyValuePair<object, object>) {
				var KVP = Object as KeyValuePair<object, object>?;
				Add ((KVP.Value.Key ?? Count).ToString (), KVP.Value.Value);	
			} 
			else {
				Add (Count.ToString (), Object);
			}
        }

        public virtual void CopyTo(jsObject Object, params string[] Keys) {
            foreach (var K in Keys){
                Object.Set(K, this.Get(K));
            }
        }

        public virtual void CopyTo(jsObject Object) {
            ForEach((k, v) => Object.AssignValue(k, v));
        }

        public void ForEach(Action<string, object> Action) {
            foreach (var Pair in this) {
                Action(Pair.Key, Pair.Value);
            }
        }

        public void ForEach(string Key, Action<string, object> Action) {
            ForEach<object>(Key, Action);
        }

        public void ForEach<T>(Action<string, T> Action) where T : class {
            foreach (var Pair in this) {
                T V = Pair.Value as T;

                if (V != default(T))
                    Action(Pair.Key, (T)Pair.Value);
            }
        }

        public bool ForEach<T>(Func<string, T, bool> Action) where T : class {
            foreach (var Pair in this) {
                var V = Pair.Value as T;

                if (V != null && Action(Pair.Key, V))
                    return true;
            }
            return false;
        }

        public void ForEach<T>(string Key, Action<string, T> Action) where T : class {
            var Obj = Get<jsObject>(Key);

            if (Obj == null)
                return;

            foreach (var Pair in Obj) {
                var V = Pair.Value as T;

                if (V != null)
                    Action(Pair.Key, V);
            }
        }

        public jsObject Sort() {
            var Keys = this.Keys.ToList();

            if (Keys.Count > 0)
                Keys.Sort();

            var Out = new jsObject();
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
            return this.ToString(true).Replace("\t", "&emsp;")
                                  .Replace("\r\n", "<br/>");
        }

        public override string ToString() {
            return Stringify(this, false);
        }

        public virtual string ToString(bool HumanFormat) {
            return Stringify(this, HumanFormat);
        }

        public static string Stringify(jsObject This, bool HumanFormat) {
            if (HumanFormat) {
                return Stringify(new StringBuilder(), This, null, 1).ToString();
            }
            else {
                return Stringify(new StringBuilder(), This, null).ToString();
            }
        }

        public static StringBuilder Stringify(StringBuilder Output, jsObject This, jsObject Parent) {
            Output.Append(This.IsArray ? '[' : '{');

            int Index = 1;
            foreach (var Pair in This) {
                var Key = Pair.Key;
                var Value = Pair.Value;

                if (Object.ReferenceEquals(Value, Parent))
                    continue;

                if (!This.IsArray) {
                    Output.AppendFormat("\"{0}\":", Pair.Key);
                }

                if (Value is jsComplex) {
                    jsComplex.Stringify(Output, Value as jsComplex, This);
                }
                else if (Value is jsObject) {
                    Stringify(Output, Value as jsObject, This);
                }
                else if (Value is bool) {
                    Output.Append(Value.ToString().ToLower());
                }
                else if (Value != null) {
                    Output.AppendFormat("\"{0}\"", Value.ToString().Escape());
                }
                else continue;

                if (Index != This.Count) {
                    Output.Append(",");
                    Index++;
                }
            }
            Output.Append(This.IsArray ? "]" : "}");

            return Output;
        }

        public static StringBuilder Stringify(StringBuilder Output, jsObject This, jsObject Parent, int Tabs) {
            Output.AppendLine(This.IsArray ? "[" : "{").Append('\t', Tabs);

            int Index = 1;
            foreach (var Pair in This) {
                var Key = Pair.Key;
                var Value = Pair.Value;

                if (Object.ReferenceEquals(Value, Parent))
                    continue;

                if (!This.IsArray) {
                    Output.AppendFormat("\"{0}\":", Pair.Key);
                }

                if (Value is jsComplex) {
                    jsComplex.Stringify(Output, Value as jsComplex, This, Tabs + 1);
                }
                else if (Value is jsObject) {
                    Stringify(Output, Value as jsObject, This, Tabs + 1);
                }
                else if (Value is bool) {
                    Output.Append(Value.ToString().ToLower());
                }
                else if (Value != null) {
                    Output.AppendFormat("\"{0}\"", Value.ToString().Escape());
                }
                else continue;

                if (Index != This.Count) {
                    Output.Append(",");
                    Index++;
                }

                Output.AppendLine().Append('\t', Tabs);
            }

            Output.Append(This.IsArray ? "]" : "}");

            return Output;
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

        public static jsObject FromUrl(string Url, string Data = "") {
            System.Net.WebClient Client = new System.Net.WebClient();
            if (string.IsNullOrEmpty(Data)) {
                Data = Client.DownloadString(Url);
            }
            else {
                Data = Client.UploadString(Url, Data);
            }

            return jsObject.Parse(Data);
        }

        public static jsObject FromFile(string Path) {
            if (File.Exists(Path)) {
                return new jsObject(File.ReadAllText(Path));
            }
            return new jsObject();
        }

        public static void RegisterParser<T>(ParserDelegate<T> Handler) {
            Parsers.Add(typeof(T).Name, (str) => {
                T value;

                if (Handler(str, out value))
                    return value;

                return null;
            });
        }

        public static implicit operator jsObject(string Text) {
            return Parse(Text);
        }

        public static jsObject operator +(jsObject Js, object Obj) {
            Js.Add(Obj);

            return Js;
        }

        public static jsObject operator -(jsObject Js, string Key) {
            Js[Key] = null;

            return Js;
        }
    }

    public class jsObject<T> : jsObject where T : class {
        public static Func<jsObject<T>> NewTypedObject = () => { return new jsObject<T>(); };

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