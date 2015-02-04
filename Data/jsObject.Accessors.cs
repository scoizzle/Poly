using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public partial class jsObject {
        public static Dictionary<Type, Func<string, object>> ParserCache = new Dictionary<Type, Func<string, object>>() {
            { typeof(int), 
                (str) => { 
                    int val; 
                    if (int.TryParse(str, out val)) 
                        return val;
                    return null;
                }
            },
            { typeof(bool), 
                (str) => { 
                    bool val;
                    if (bool.TryParse(str, out val))
                        return val;
                    return null;
                }
            },
            { typeof(long),
                (str) => {
                    long val;
                    return long.TryParse(str, out val) ? val : default(long);
                }
            },
            { typeof(float), 
                (str) => { 
                    float val;
                    if (float.TryParse(str, out val))
                        return val;
                    return null;
                }
            },
            { typeof(double),
                (str) => {
                    double val;
                    if (double.TryParse(str, out val))
                        return val;
                    return null;
                }
            },
            { typeof(jsObject),
                (str) => {
                    jsObject Obj = new jsObject();

                    if (Obj.Parse(str))
                        return Obj;

                    return null;
                }
            },
        };

        public static Func<jsObject> NewObject = () => { return new jsObject(); },
                                     NewArray = () => { return new jsObject() { IsArray = true }; };

        public T Get<T>(string Key) {
            T Value;
            if (StringExtensions.Contains(Key, '.')) {
                int Open = 0, Close = Key.Find('.');
                jsObject Current = this;

                while (Current != null && Open != -1 && Close != -1) {
                    var Sub = Key.Substring(Open, Close - Open);

                    if (Close == Key.Length && Current.TryGet<T>(Sub, out Value))
                        return Value;

                    if (!Current.TryGet<jsObject>(Sub, out Current))
                        return default(T);

                    Open = Close + 1;
                    Close = Key.Find('.', Open);

                    if (Close == -1) {
                        Close = Key.Length;
                    }
                }
            }
            else {
                if (this.TryGet<T>(Key, out Value))
                    return Value;
            }

            return default(T);
        }

        public T Get<T>(string Key, T Default) {
            object Value = Get<T>(Key);

            if (Value == null) {
                Set<T>(Key, Default);
                return Default;
            }

            return (T)Value;
        }

        public T Get<T>(string Key, Func<T> OnMissingHander) {
            T Value = Get<T>(Key);

            if (Value == null) {
                Set<T>(Key, Value = OnMissingHander());
            }

            return Value;
        }

        public T Get<T>(string[] Keys) {
            T Value;
            jsObject Current = this;

            for (int i = 0; Current != null && i < Keys.Length; i++) {
                var Sub = Keys[i];

                if (StringExtensions.Contains(Sub, '.')) {
                    Current = Current.Get<jsObject>(Sub);
                    continue;
                }

                if ((Keys.Length - i) == 1 && Current.TryGet<T>(Sub, out Value)) {
                    return Value;
                }

                if (!Current.TryGet<jsObject>(Sub, out Current))
                    break;
            }
            return default(T);
        }

        public T Search<T>(string Key, bool IgnoreCase = true, bool KeyIsWild = false) where T : class {
            T Value = default(T);

            if (KeyIsWild) {
                ForEach<T>((K, V) => {
                    if (Key.Compare(K, IgnoreCase)) {
                        Value = V;
                        return true;
                    }
                    return false;
                });
            }
            else {
                ForEach<T>((K, V) => {
                    if (K.Compare(Key, IgnoreCase)) {
                        Value = V;
                        return true;
                    }
                    return false;
                });
            }

            return Value;
        }

        public jsObject getObject(string Key) {
            return Get<jsObject>(Key);
        }
        
        public bool TryGet<T>(string Key, out T Value) {
            object Obj;

            if (GetValue(Key, out Obj)) {
				try {
                    Value = (T)Obj;
                    return true;
				}
				catch {
                    var str = Obj as string;
                    if (str != null) {
                        var Type = typeof(T);
                        if (ParserCache.ContainsKey(Type)) {
                            Value = (T)ParserCache[Type](str);
                            return true;
                        }
                    }
                    else {
                        Value = default(T);
                        return false;
                    }
				}
            }

            Value = default(T);
            return false;
        }

        public void Set<T>(string Key, T Value) {
            if (StringExtensions.Contains(Key, '.')) {
                jsObject Current = this;
                int Open = 0, Close = Key.Find('.');

                while (Current != null && Open != -1 && Close != -1) {
                    var Sub = Key.Substring(Open, Close - Open);

                    if (Close == Key.Length) {
                        if (Value == null) {
                            Current.Remove(Sub);
                        }
                        else {
                            Current.AssignValue<T>(Sub, Value);
                        }
                        return;
                    }

                    jsObject Next;
                    if (!Current.TryGet<jsObject>(Sub, out Next)) {
                        Next = new jsObject();
                        Current.AssignValue(Sub, Next);
                    }
                    Current = Next;

                    Open = Close + 1;
                    Close = Key.Find('.', Open);

                    if (Close == -1) {
                        Close = Key.Length;
                    }
                }
            }

            if (Value == null) {
                this.Remove(Key);
            }
            else {
                this.AssignValue(Key, Value);
            }
        }

        public void Set<T>(string[] Keys, T Value) {
            jsObject Current = this;

            for (int i = 0; i < Keys.Length; i++) {
                var Sub = Keys[i];

                if ((Keys.Length - i) == 1) {
                    if (Sub.Contains('.')) {
                        Current.Set(Sub, Value);
                    }
                    else {
                        Current.AssignValue<T>(Sub, Value);
                    }
                    break;
                }

                var Next = Current.Get<jsObject>(Sub);

                if (Next == null) {
                    Next = new jsObject();

                    if (Sub.Contains('.')) {
                        Current.Set(Sub, Next);
                    }
                    else {
                        Current.AssignValue<jsObject>(Sub, Next);
                    }
                }

                Current = Next;
            }
        }

        public virtual bool GetValue(string Key, out object Value) {
            return TryGetValue(Key, out Value);
        }

        public virtual void AssignValue<T>(string Key, T Value) {
            base[Key] = Value;
        }
    }
}
