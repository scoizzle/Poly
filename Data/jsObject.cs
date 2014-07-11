using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public partial class jsObject : Dictionary<string, object> {
        public static jsObject Null = null;

        public new object this[string Key] {
            get {
                return Get<object>(Key);
            }
            set {
                Set(Key, value);
            }
        }

        public object this[params string[] Keys] {
            get {
                return Get<object>(Keys);
            }
            set {
                Set<object>(Keys, value);
            }
        }

        public jsObject() { }

        public jsObject(string Json) {
            Parse(Json);
        }

        public jsObject(params object[] KeyValuePairs) {
            for (int i = 0; i < KeyValuePairs.Length - 1; i += 2) {
                Set(KeyValuePairs[i].ToString(), KeyValuePairs[i + 1]);
            }
        }

        public bool IsEmpty {
            get {
                return this.Count == 0;
            }
        }

        public bool IsArray { get; set; }

        public void Add(object Object) {
            Add(Count.ToString(), Object);
        }

        public void CopyTo(jsObject Object) {
            ForEach((K, V) => {
                Object[K] = V;
            });
        }

        public void ForEach(Action<string, object> Action) {
            var List = this.ToList();

            for (int i = 0; i < List.Count; i++) {
                Action(List[i].Key, List[i].Value);
            }

            List = null;
        }

        public void ForEach(string Key, Action<string, object> Action) {
            ForEach<object>(Key, Action);
        }

        public void ForEach<T>(Action<string, T> Action) {
            var List = this.ToList();

            for (int i = 0; i < List.Count; i++) {
                if (List[i].Value is T) 
                    Action(List[i].Key, (T)List[i].Value);
            }

            List = null;
        }

        public bool ForEach<T>(Func<string, T, bool> Action) {
            foreach (var Pair in this) {
                if (!(Pair.Value is T))
                    continue;

                if (Action(Pair.Key, (T)Pair.Value))
                    return true;
            }
            return false;
        }

        public void ForEach<T>(string Key, Action<string, T> Action) {
            var Obj = this[Key] as jsObject;

            if (Obj == null)
                return;

            foreach (var Pair in Obj) {
                var Val = Pair.Value;

                if (!(Val is T))
                    continue;

                Action(Pair.Key, (T)Val);
            }
        }

        public override string ToString() {
            return Stringify(this, false);
        }

        public virtual string ToString(bool HumanFormat) {
            return Stringify(this, HumanFormat);
        }

        public string ToPostString() {
            StringBuilder Output = new StringBuilder();

            ForEach((K, V) => {
                Output.AppendFormat("{0}={1}&", PostEncode(K), PostEncode(V.ToString()));
            });

            return Output.ToString(0, Output.Length - 1);
        }

        public static string Stringify(jsObject This, bool HumanFormat, int Reserved = 1) {
            StringBuilder Output = new StringBuilder();

            Output.Append(This.IsArray ? '[' : '{');

            if (HumanFormat) {
                Output.AppendLine();
                Output.Append('\t', Reserved);
            }

            int Index = 0;
            foreach (var Pair in This) {
                var K = Pair.Key;
                var V = Pair.Value;

                if (!This.IsArray)
                    Output.AppendFormat("'{0}':", K);

                var Obj = V as jsObject;

                if (Obj != null) {
                    Output.Append(
                        Stringify(Obj, HumanFormat, Reserved + 1)
                    );
                }
                else if (V is string) {
                    Output.AppendFormat("'{0}'", V);
                }
                else {
                    Output.Append(V);
                }

                if (This.Count > 1 && (This.Count - Index) != 1) {
                    Output.Append(',');
                    Index++;
                }
                else {
                    Reserved--;
                }

                if (HumanFormat) {
                    Output.Append(Environment.NewLine);
                    Output.Append('\t', Reserved);
                }
            };

            Output.Append(This.IsArray ? ']' : '}');

            return Output.ToString();
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

            jsObject Object = new jsObject();

            if (Object.Parse(Data)) {
                return Object;
            }

            return default(jsObject);
        }

        public static jsObject FromFile(string Path) {
            if (File.Exists(Path)) {
                return new jsObject(File.ReadAllText(Path));
            }
            return new jsObject();
        }

        public static implicit operator jsObject(string Text) {
            jsObject Object = new jsObject();

            if (Object.Parse(Text)) {
                return Object;
            }

            return default(jsObject);
        }
    }

    public class jsObject<T> : jsObject {
        public static Func<jsObject<T>> NewTypedObject = () => { return new jsObject<T>(); };

        public new T this[string Key] {
            get {
                return Get<T>(Key);
            }
            set {
                Set(Key, value);
            }
        }

        public new T this[params string[] Keys] {
            get {
                return Get<T>(Keys);
            }
            set {
                Set<T>(Keys, value);
            }
        }

        public void Add(string Key, T Obj) {
            Set(Key, Obj);
        }

        public void ForEach(Action<string, T> Action) {
            ForEach<T>(Action);
        }
    }
}