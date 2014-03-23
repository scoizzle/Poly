using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public partial class jsObject : Dictionary<string, object> {
        private static Type[] ParseFuncArgTypes = new Type[] { typeof(string) };

        public static Func<jsObject> NewObject = () => {
            return new jsObject();
        };

        public static Func<jsObject> NewArray = () => {
            return new jsObject() { IsArray = true };
        };

        public string KeySeperator = ".";
        public bool IsArray = false;

        public jsObject(string Json = "") : base() {
            Parse(Json);
        }

        public jsObject(params object[] KeyValuePairs) {
            if ((KeyValuePairs.Length % 2) == 1)
                return;

            for (int Index = 0; Index < KeyValuePairs.Length; Index += 2) {
                var Key = KeyValuePairs[Index] as string;

                if (string.IsNullOrEmpty(Key))
                    continue;

                var Value = KeyValuePairs[Index + 1];

                this[Key] = Value;
            }
        }

        public new object this[string Key] {
            get {
                return OnGet(Key);
            }
            set {
                OnSet(Key, value);
            }
        }

        public object this[params string[] Key] {
            get {
                return OnGet(Key);
            }
            set {
                OnSet(Key, value);
            }
        }

        public bool IsEmpty {
            get {
                return this.Count == 0;
            }
        }

        public jsObject Base {
            get {
                return this as jsObject;
            }
        }

        private Dictionary<string, object> Storage {
            get {
                return (Dictionary<string, object>)(this);
            }
        }

        public void Add(object Value) {
            this[Count.ToString()] = Value;
        }

        public void AddArray(string Key, params object[] Items) {
            var Obj = new jsObject() {
                IsArray = true
            };

            for (int n = 0; n < Items.Length; n++) {
                Obj.Add(Items[n]);
            }

            Set(Key, Obj);
        }

        public void CopyTo(jsObject Object) {
            var Array = this.ToArray();

            for (int Index = 0; Index < Array.Length; Index++) {
                Object[Array[Index].Key] = Array[Index].Value;
            }
        }

        public void ForEach(Action<string, object> Action) {
            ForEach<object>(Action);
        }

        public void ForEach<T>(Action<string, T> Action) where T : class {
            foreach (var Pair in this) {
                var Temp = Pair.Value as T;

                if (Temp != null)
                    Action(Pair.Key, Temp);
            }
        }

        public virtual object OnGet(string Key) {
            if (Key.Contains(KeySeperator)) {
                return Get<object>(Key);
            }
            else {
                object Obj = null;

                if (TryGetValue(Key, out Obj))
                    return Obj;

                return null;
            }
        }

        public virtual object OnGet(string[] Key) {
            if (Key.Length == 1) {
                if (Key[0].Contains(KeySeperator)) {
                    return Get<object>(Key[0]);
                }
                else if (base.ContainsKey(Key[0])) {
                    return base[Key[0]];
                }
                else return null;
            }

            return Get<object>(Key);
        }

        public virtual void OnSet(string Key, object Value) {
            if (Key.Contains(KeySeperator)) {
                Set(Key, Value);
            }
            else if (Value == null) {
                Remove(Key);
            }
            else {
                Storage[Key] = Value;
            }
        }

        public virtual void OnSet(string[] Key, object Value) {
            if (Key.Length == 1) {
                if (Key[0].Contains(KeySeperator)) {
                    Set(Key[0].Split(KeySeperator), Value);
                }
                else {
                    if (Value != null) {
                        Storage[Key[0]] = Value;
                    }
                    else if (ContainsKey(Key[0])) {
                        Remove(Key[0]);
                    }
                }
            }
            else {
                Set(Key, Value);
            }
        }

        public override string ToString() {
            return ToString(false);
        }

        public virtual string ToString(bool HumanFormat = false) {
            return jsObject.Stringify(this, HumanFormat);
        }

        public static string Stringify(jsObject This, bool HumanFormat, int Reserved = 1) {
            string CurTab = new string('\t', Reserved);
            StringBuilder Output = new StringBuilder();

            if (This.IsArray) {
                Output.Append("[");
            }
            else {
                Output.Append("{");
            }

            if (HumanFormat) {
                Output.AppendLine().Append(CurTab);
            }

            foreach (KeyValuePair<string, object> Pair in This) {
                if (!Pair.Key.IsNumeric()) {
                    Output.AppendFormat("'{0}':", Pair.Key);
                }

                if ((Pair.Value as jsObject) != null) {
                    Output.Append(
                        jsObject.Stringify((Pair.Value as jsObject), HumanFormat, Reserved + 1)
                    );
                }
                else if (Pair.Value is string) {
                    Output.AppendFormat("'{0}'", Pair.Value.ToString().Escape());
                }
                else {
                    Output.Append(Pair.Value);
                }

                if (This.Count > 1 && Pair.Key != This.Last().Key) {
                    Output.Append(',');
                }
                else {
                    CurTab = CurTab.Substring(0, CurTab.Length - 1);
                }

                if (HumanFormat) {
                    Output.Append(Environment.NewLine).Append(CurTab);
                }
            }

            if (This.IsArray) {
                Output.Append("]");
            }
            else {
                Output.Append("}");
            }


            return Output.ToString();
        }

        public static jsFile FromFile(string fileName) {
            jsFile Object = new jsFile();

            if (Object.Load(fileName)) {
                return Object;
            }

            return default(jsFile);
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

        public static jsObject FromEmbeded(string Name) {
            Data.jsObject Obj = "";
            var asm = Assembly.GetCallingAssembly();

            try {
                using (StreamReader Reader = new StreamReader(asm.GetManifestResourceStream(Name))) {
                    Obj = Reader.ReadToEnd();
                }
            }
            catch {
                return "";
            }

            return Obj;
        }

        public static implicit operator jsObject(string Text) {
            jsObject Object = new jsObject();

            if (Object.Parse(Text)) {
                return Object;
            }

            return default(jsObject);
        }
    }

    public class jsObject<T> : jsObject where T : class {
        public static Func<jsObject<T>> NewTypedObject = () => {
            return new jsObject<T>();
        };

        public jsObject(string Json = "", string KeySeperator = ".") : base(Json) {
            this.KeySeperator = KeySeperator;
        }

        public new T this[string Key] {
            get {
                return (T)base[Key];
            }
            set {
                base[Key] = value;
            }
        }

        public new T this[params string[] Key] {
            get {
                return (T)base[Key];
            }
            set {
                base[Key] = value;
            }
        }

        public void Add(string Key, T Obj) {
            this[Key] = Obj;
        }

        public void ForEach(Action<string, T> Action) {
			base.ForEach ((K, V) => {
				Action(K, V as T);
			});
        }
    }
}