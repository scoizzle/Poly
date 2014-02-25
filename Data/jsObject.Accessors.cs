using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public partial class jsObject : Dictionary<string, object> {
        private object Get(string Key) {
            if (Key.Contains(KeySeperator))
                return Get(Key.Split(KeySeperator));
            if (Storage.ContainsKey(Key))
                return Storage[Key];
            return null;
        }

        private object Get(params string[] KeyList) {
            return Get<object>(KeyList);
        }

        public R Get<R>(params string[] KeyList) {
            jsObject Current = this;

            if (KeyList.Length == 1 && KeyList[0].Contains(KeySeperator)) {
                return Get<R>(KeyList[0].Split(KeySeperator));
            }

            for (int i = 0; i < KeyList.Length; i++) {
                string Key = KeyList[i];
                object Value = null;

                if (!Current.Storage.TryGetValue(Key, out Value)) {
                    break;
                }

                if ((KeyList.Length - i) == 1) {
                    if (!Current.ContainsKey(Key))
                        break;

                    if (Value is R) {
                        return (R)(Value);
                    }
                    else if (Value is string) {
                        var Method = typeof(R).GetMethod("Parse", ParseFuncArgTypes);

                        if (Method != null && Method.IsStatic) {
                            Value = Method.Invoke(null, new object[] { Value });
                            Current.Set(Key, Value);
                            return (R)Value;
                        }
                    }
                    else break;
                }
                else if (Value is jsObject) {
                    Current = Value as jsObject;
                }
                else break;
            }

            return default(R);
        }

        public R Get<R>(string Key, R Default) {
            object Return = Get<R>(Key.Split(KeySeperator));

            if (Return == null) {
                Set(Key, Default);
                return Default;
            }

            return (R)Return;
        }

        public R Get<R>(string Key, Func<R> OnMissingHander) {
            object Return = Get<R>(Key.Split(KeySeperator));

            if (Return == null) {
                Return = OnMissingHander();
                Set(Key, Return);
            }

            return (R)Return;
        }

        public R Search<R>(string Key, bool IgnoreCase = true, bool KeyIsWild = false) {
            foreach (var Pair in this) {
                if (Pair.Value is R) {
                    if (KeyIsWild && Pair.Key.Compare(Key, IgnoreCase)) {
                        return (R)Pair.Value;
                    }
                    else if (Key.Compare(Pair.Key, IgnoreCase)) {
                        return (R)Pair.Value;
                    }
                }
            }

            return default(R);
        }

        public bool Set(string[] KeyList, object Value) {
            jsObject Current = this;

            if (KeyList.Length == 0 && Value is jsObject) {
                (Value as jsObject).CopyTo(Current);
                return true;
            }

            for (int Index = 0; Index < KeyList.Length; Index++) {
                string Key = KeyList[Index];

                if (string.IsNullOrEmpty(Key))
                    continue;

                if (Current == null)
                    return false;

                if (Index == KeyList.Length - 1) {
                    Current.Storage[Key] = Value;
                    return true;
                }
                else if (!Current.ContainsKey(Key)) {
                    Current[Key] = new jsObject();
                    Current = Current[Key] as jsObject;
                }
                else if ((Current[Key] as jsObject) != null) { 
                    Current = (Current[Key] as jsObject);
                }
                else {
                    break;
                }
            }

            return false;
        }

        public bool Set(string Key, object Value) {
            this[Key] = Value;
            return true;
        }

        public int getInt(params string[] Key) {
            return Convert.ToInt32(Get<object>(Key));
        }

        public double getDouble(params string[] Key) {
            return Convert.ToDouble(Get<object>(Key));
        }

        public long getLong(params string[] Key) {
            return Convert.ToInt64(Get<object>(Key));
        }

        public bool getBool(params string[] Key) {
            return Get<bool>(Key);
        }

        public string getString(params string[] Key) {
            var Obj = Get<object>(Key);

            if (Obj == null)
                return "";

            return Obj.ToString();
        }

        public jsObject getObject(params string[] Key) {
            return Get<jsObject>(Key);
        }
    }
}
