using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public partial class jsObject {
        public delegate object ParserDelegate(string Value);
        public static Dictionary<Type, ParserDelegate> Parsers;

        public static Func<jsObject> NewObject = () => { return new jsObject(); }
        ,
                                     NewArray = () => { return new jsObject() { IsArray = true }; };


        public object Get(string Key) {
            return Get(Key.Split("."));
        }

        public object Get(params string[] Keys) {
            int i = 0;
            object Value;
            jsObject Current = this;

            do {
                if (Current.TryGet(Keys[i], out Value)) {
                    if (Keys.Length - i == 1)
                        break;

                    if (Value is jsObject)
                        Current = Value as jsObject;
                    else
                        return null;
                }
            }
            while (++i < Keys.Length);

            return Value;
        }

        public object Get(IEnumerable<string> Keys) {
            object Value = null;
            jsObject Current = this;
            int I = 0, Len = Keys.Count() - 1;

            foreach (var K in Keys) {
                if (Current.TryGet(K, out Value)) {
                    if (I++ == Len)
                        break;
                    else
                    if (Value is jsObject)
                        Current = Value as jsObject;
                }
                else return null;
            }

            return Value;
        }

        public T Get<T>(string Key) {
            return Get<T>(Key.Split("."));
        }

        public T Get<T>(params string[] Key) {
            var Value = Get(Key);

            if (Value != null) {
                if (Value is T)
                    return (T)(Value);

                if (Value is string && Parsers.ContainsKey(typeof(T))) {
                    Value = Parsers[typeof(T)](Value as string);

                    Set(Key, Value);

                    return (T)(Value);
                }
            }

            return default(T);
        }

        public T Get<T>(IEnumerable<string> Key) {
            var Value = Get(Key);

            if (Value != null) {
                if (Value is T)
                    return (T)(Value);

                if (Value is string && Parsers.ContainsKey(typeof(T))) {
                    Value = Parsers[typeof(T)](Value as string);

                    Set(Key, Value);

                    return (T)(Value);
                }
            }

            return default(T);
        }

        public T Search<T>(string Key, bool KeyIsWild = false) where T : class {
            T Value = default(T);

            if (KeyIsWild) {
                ForEach<T>((K, V) => {
                    if (Key.Match(K) != null) {
                        Value = V;
                        return true;
                    }
                    return false;
                });
            }
            else {
                ForEach<T>((K, V) => {
                    if (K.Match(Key) != null) {
                        Value = V;
                        return true;
                    }
                    return false;
                });
            }

            return Value;
        }

        public void Set(string Key, object Value) {
            AssignValue(Key, Value);
        }

        public void Set(IEnumerable<string> Keys, object Value) {
            object Object;
            jsObject Current = this;
            int I = 1, Len = Keys.Count();

            foreach (var K in Keys) {
                if (I++ == Len)
                    Current.AssignValue(K, Value);
                else if (Current.TryGet(K, out Object)) {
                    if (Object is jsObject)
                        Current = Object as jsObject;
                    else break;
                }
                else {
                    Current.AssignValue(K, Current = new jsObject());
                }
            }
        }

        public void Set(string[] Keys, object Value) {
            int i = 0;
            object Object;
            jsObject Current = this;

            do {
                if (Keys.Length - i == 1) {
                    Current.AssignValue(Keys[i], Value);
                }
                else if (Current.TryGet(Keys[i], out Object)) {
                    if (Object is jsObject)
                        Current = Object as jsObject;
                    else break;
                }
                else {
                    Current.AssignValue(Keys[i], Current = new jsObject());
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
                else if (Val is string && Parsers.ContainsKey(typeof(T))) {
                    Val = Parsers[typeof(T)](Val as string);

                    if (Val is T) {
                        Set(Key, Val);
                        Value = (T)(Val);
                        return true;
                    }
                }
            }

            Value = default(T);
            return false;
        }

        public T GetValue<T>(string Key) {
            T Val;

            if (TryGet<T>(Key, out Val))
                return Val;

            return default(T);
        }

        public virtual void AssignValue<T>(string Key, T Value) {
            base[Key] = Value;
        }
    }
}