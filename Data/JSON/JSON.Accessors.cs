using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Data {
    public partial class JSON {
        public delegate bool ParserDelegate<T>(string Text, out T Value);
        public delegate object WrapperDelegate(string Text);

        public static KeyValueCollection<WrapperDelegate> Parsers;

        public static Func<JSON> NewObject = () => { return new JSON(); },
                                     NewArray = () => { return new JSON { IsArray = true }; };


        new public object Get(string Key) {
            return Get(Key.Split(KeySeperatorCharacter));
        }

        public object Get(params string[] Keys) {
            int i = 0;
			int len = Keys.Length;

            object Value;
            JSON Current = this;

			do {
				if (Current.TryGet(Keys[i], out Value)) {
					if (len - i == 1)
						break;

					if (Value is JSON)
						Current = Value as JSON;
					else
						return null;
				}
			}
			while (++i < len);

            return Value;
        }
        
        public T Get<T>(string Key) {
            return Get<T>(Key.Split(KeySeperatorCharacter));
        }

        public T Get<T>(params string[] Key) {
            var Value = Get(Key);

            if (Value != null) {
                if (Value is T)
                    return (T)(Value);

                if (Value is string) { 
                    var Delegate = Parsers[typeof(T).Name];

                    if (Delegate != null) {
                        Value = Delegate(Value as string);

                        Set(Key, Value);

                        return (T)(Value);
                    }
                }
            }

            return default(T);
        }

        public T Get<T>(IEnumerable<string> Key) {
            var Value = Get(Key.ToArray());

            if (Value != null) {
                if (Value is T)
                    return (T)(Value);

                if (Value is string) {
                    var Delegate = Parsers[typeof(T).Name];

                    if (Delegate != null) {
                        Value = Delegate(Value as string);

                        Set(Key, Value);

                        return (T)(Value);
                    }
                }
            }

            return default(T);
        }

        new public void Set(string Key, object Value) {
            AssignValue(Key, Value);
        }

        public void Set(IEnumerable<string> Keys, object Value) {
            object Object;
            JSON Current = this;
            int I = 1, Len = Keys.Count();

            foreach (var K in Keys) {
                if (I++ == Len)
                    Current.AssignValue(K, Value);
                else if (Current.TryGet(K, out Object)) {
                    if (Object is JSON)
                        Current = Object as JSON;
                    else break;
                }
                else {
                    Current.AssignValue(K, Current = new JSON());
                }
            }
        }

        public void Set(string[] Keys, object Value) {
            int i = 0;
            object Object;
            JSON Current = this;

            do {
                if (Keys.Length - i == 1) {
                    Current.AssignValue(Keys[i], Value);
                }
                else if (Current.TryGet(Keys[i], out Object)) {
                    if (Object is JSON)
                        Current = Object as JSON;
                    else break;
                }
                else {
                    Current.AssignValue(Keys[i], Current = new JSON());
                }
            }
            while (++i < Keys.Length);
        }

        public virtual bool TryGet(string Key, out object Value) {
            return TryGetValue(Key, out Value);
        }

        public virtual bool TryGet<T>(string Key, out T Value) {
            object Val;

            if (TryGet(Key, out Val)) {
                if (Val is T) {
                    Value = (T)(Val);
                    return true;
                }
                else
                if (Val is string) {
                    var Delegate = Parsers[typeof(T).Name];

                    if (Delegate != null) {
                        Val = Delegate(Val as string);

                        Set(Key, Val);
                        Value = (T)(Val);
                        return true;
                    }
                }
            }

            Value = default(T);
            return false;
        }

        public T GetValue<T>(string Key) where T : class {
            return base[Key] as T;
        }

        public virtual void AssignValue(string Key, object Value) {
            base[Key] = Value;
        }
    }
}