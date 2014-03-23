using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public partial class jsObject : Dictionary<string, object> {
        private static Dictionary<Type, Func<string, object>> ParserCache = new Dictionary<Type, Func<string, object>>() {
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
                    return float.TryParse(str, out val) ? val : float.NaN; 
                }
            },
            { typeof(double),
                (str) => {
                    double val;
                    return double.TryParse(str, out val) ? val : double.NaN;
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
        
        public R Get<R>(string Key) {
            if (string.IsNullOrEmpty(Key))
                return default(R);

            jsObject Current = this;

            for (int Index = 0; Index != -1; Index += KeySeperator.Length) {
                var SubIndex = Key.IndexOf(KeySeperator, Index);

                var Sub = SubIndex == -1 ?
                    Key.Substring(Index, Key.Length - Index) :
                    Key.SubString(Index, SubIndex - Index);

                object Value = null;

                if (!Current.Storage.TryGetValue(Sub, out Value)) {
                    break;
                }

                if (SubIndex == -1) {
                    if (Value is R) {
                        return (R)(Value);
                    }
                    else {
                        Func<string, object> Method;

                        if (ParserCache.TryGetValue(typeof(R), out Method)) {
                            Value = Method(Value.ToString());
                            Set(Sub, Value);
                            return (R)(Value);
                        }
                        else break;
                    }
                }
                else {
                    var Obj = Value as jsObject;

                    if (Obj != null) {
                        Current = Obj;
                    }
                    else break;
                }

                Index = SubIndex;
            }

            return default(R);
        }

        public R Get<R>(string[] KeyList) {
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
                    if (Value is R) {
                        return (R)(Value);
                    }
                    else if (Value is string) {
                        Func<string, object> Method;

                        if (ParserCache.TryGetValue(typeof(R), out Method)) {
                            Value = Method(Value as string);
                            Set(Key, Value);
                            return (R)(Value);
                        }
                    }
                    else break;
                }
                else {
                    var Obj = Value as jsObject;

                    if (Obj != null) {
                        Current = Obj;
                    }
                    else break;
                }
            }

            return default(R);
        }

        public R Get<R>(string Key, R Default) {
            object Return = Get<R>(Key);

            if (Return == null) {
                Set(Key, Default);
                return Default;
            }

            return (R)Return;
        }

        public R Get<R>(string Key, Func<R> OnMissingHander) {
            object Return = Get<R>(Key);

            if (Return == null) {
                Return = OnMissingHander();
                Set(Key, Return);
            }

            return (R)Return;
        }

        public R Search<R>(string Key, bool IgnoreCase = true, bool KeyIsWild = false) {
            foreach (var Pair in this) {
                if (Pair.Value is R) {
                    if (KeyIsWild) {
                        if (Pair.Key.Compare(Key, IgnoreCase)) {
                            return (R)Pair.Value;
                        }
                    }
                    else if (Key.Compare(Pair.Key, IgnoreCase)) {
                        return (R)Pair.Value;
                    }
                }
            }

            return default(R);
        }

        public bool TryGetValue<T>(string Key, out T Value) where T : class {
            object Obj;

            if (base.TryGetValue(Key, out Obj)) {
                if (Obj is T) {
                    Value = (T)Obj;
                    return true;
                }
            }

            Value = default(T);
            return false;
        }

        public bool Set(string Key, object Value) {
            if (string.IsNullOrEmpty(Key))
                return false;

            jsObject Current = this;

            for (int Index = 0; Index != -1; Index += KeySeperator.Length) {
                var SubIndex = Key.IndexOf(KeySeperator, Index);

                while (SubIndex > 0 && Key[SubIndex - 1] == '\\') {
                    SubIndex = Key.IndexOf(KeySeperator, SubIndex + KeySeperator.Length);
                }

                if (SubIndex == -1) {
                    Current.Storage[Key.Substring(Index, Key.Length - Index)] = Value;
                    return true;
                }
                else {
                    object Obj = null;

                    string Sub = Key.SubString(Index, SubIndex - Index);

                    if (Current.TryGetValue(Sub, out Obj)) {
                        jsObject jsObj = Obj as jsObject;

                        if (jsObj != null) {
                            Current = jsObj;
                        }
                        else break;
                    }
                    else {
                        var jsObj = new jsObject();

                        Current.Storage[Sub] = jsObj;

                        Current = jsObj;
                    }
                }

                Index = SubIndex;
            }

            return false;
        }

        public bool Set(string[] KeyList, object Value) {
            jsObject Current = this;

            for (int Index = 0; Index < KeyList.Length; Index++) {
                string Key = KeyList[Index];

                if (string.IsNullOrEmpty(Key))
                    continue;

                if ((KeyList.Length - Index) == 1) {
                    Current.Storage[Key] = Value;
                    return true;
                }
                else {
                    object Obj = null;

                    if (Current.TryGetValue(Key, out Obj)) {
                        jsObject jsObj = Obj as jsObject;

                        if (jsObj != null) {
                            Current = jsObj;
                        }
                        else break;
                    }
                    else {
                        var jsObj = new jsObject();

                        Current.Storage[Key] = jsObj;

                        Current = jsObj;
                    }
                }
            }

            return false;
        }

        public jsObject getObject(string Key) {
            return Get<jsObject>(Key);
        }

        public jsObject getObject(params string[] Key) {
            return Get<jsObject>(Key);
        }
    }
}
