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
            if (StringExtensions.Contains(Key, '.')) {
                int Open = 0, Close = Key.Find('.');
                jsObject Current = this;

                while (Current != null && Open != -1 && Close != -1) {
                    var Sub = Key.Substring(Open, Close - Open);

                    if (Close == Key.Length)
                        return Current.SearchForItem<T>(Sub);

                    var Next = Current.SearchForItem<jsObject>(Sub);

                    if (Next == null) {
                        return default(T);
                    }
                    Current = Next;

                    Open = Key.Find('.', Close + 1) + 1;

                    if (Open > 0) {
                        Close = Key.Find('.', Open);

                        if (Close == -1) {
                            Close = Key.Length;
                        }
                    }
                    else {
                        Open = Close + 1;
                        Close = Key.Length;
                    }
                }

                Key = Key.Descape();
            }

            return SearchForItem<T>(Key);
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
                Value = OnMissingHander();
                Set<T>(Key, Value);
            }

            return (T)Value;
        }

        public T Get<T>(string[] Keys) {
            jsObject Current = this;

            for (int i = 0; Current != null && i < Keys.Length; i++) {
                var Sub = Keys[i];

                if (StringExtensions.Contains(Sub, '.')) {
                    Current = Current.Get<jsObject>(Sub);
                    continue;
                }

                if ((Keys.Length - i) == 1) {
                    return Current.SearchForItem<T>(Sub);
                }

                Current = Current.SearchForItem<jsObject>(Sub);
            }
            return default(T);
        }

        public T Search<T>(string Key, bool IgnoreCase = true, bool KeyIsWild = false) {
            var Enum = GetEnumerator();

            while (Enum.MoveNext()) {
                if (!typeof(T).IsAssignableFrom(Enum.Current.Value.GetType()))
                    continue;

                if (KeyIsWild ?
                    Key.Compare(Enum.Current.Key, IgnoreCase) :
                    Enum.Current.Key.Compare(Key, IgnoreCase)) {
                    return (T)Enum.Current.Value;
                }
            }           

            return default(T);
        }

        public jsObject getObject(string Key) {
            return Get<jsObject>(Key);
        }

        public bool TryGetValue<T>(string Key, out T Value) {
            object Obj;

            if (base.TryGetValue(Key, out Obj)) {
                Value = (T)Obj;
                return true;
            }

            Value = default(T);
            return false;
        }

        public void Set<T>(string Key, T Value) {
            if (StringExtensions.Contains(Key, '.')) {
                int Open = 0, Close = Key.Find('.');
                jsObject Current = this;

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

                    var Next = Current.SearchForItem<jsObject>(Sub);

                    if (Next == null) {
                        Next = new jsObject();
                        Current.AssignValue(Sub, Next);
                    }
                    Current = Next;

                    Open = Key.Find('.', Close + 1) + 1;

                    if (Open > 0) {
                        Close = Key.Find('.', Open);

                        if (Close == -1) {
                            Close = Key.Length;
                        }
                    }
                    else {
                        Open = Close + 1;
                        Close = Key.Length;
                    }
                }

                Key = Key.Descape();
            }

            if (Value == null) {
                Remove(Key);
            }
            else {
                AssignValue(Key, Value);
            }
        }

        public void Set<T>(string[] Keys, T Value) {
            jsObject Current = this;

            for (int i = 0; i < Keys.Length; i++) {
                var Sub = Keys[i];

                if ((Keys.Length - i) == 1) {
                    Current.AssignValue<T>(Sub, Value);
                    break;
                }

                var Next = Current.Get<jsObject>(Sub);

                if (Next == null) {
                    Next = new jsObject();
                    Current.AssignValue<jsObject>(Sub, Next);
                }

                Current = Next;
            }
        }

        private void AssignValue<T>(string Key, T Value) {
            base[Key] = Value;
        }

        private T SearchForItem<T>(string Key) {
            object Item;
            if (base.TryGetValue(Key, out Item)) {
                if (Item is T) {
                    return (T)Item;
                }
                else if (typeof(T).IsAssignableFrom(Item.GetType())) {
                    return (T)Item;
                }

                if (Item is string) {
                    Func<string, object> Parser;

                    if (jsObject.ParserCache.TryGetValue(typeof(T), out Parser)) {
                        object Obj = Parser((string)Item);

                        if (Obj != null) {
                            AssignValue(Key, Obj);

                            return (T)Obj;
                        }
                    }
                }
            }
            return default(T);
        }
    }
}
