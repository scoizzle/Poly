using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public class jsComplex : jsObject {
        static Dictionary<Type, Dictionary<string, Tuple<Func<object, object>, Action<object, object>>>> Cache =
            new Dictionary<Type, Dictionary<string, Tuple<Func<object, object>, Action<object, object>>>>();

        static jsComplex() {
            foreach (var Mod in AppDomain.CurrentDomain.GetAssemblies()) {
                foreach (var T in Mod.GetTypes()) {
                    if (typeof(jsComplex).IsAssignableFrom(T)) {
                        InitType(T);
                    }
                }
            }
        }

        static Dictionary<string, Tuple<Func<object, object>, Action<object, object>>> InitType(Type T) {
            var TypeCache = new Dictionary<string, Tuple<Func<object, object>, Action<object, object>>>();

            foreach (var Field in T.GetFields()) {
                TypeCache.Add(Field.Name, new Tuple<Func<object, object>, Action<object, object>>(
                    Field.GetValue,
                    Field.SetValue
                ));
            }

            return TypeCache;
        }

        Dictionary<string, Tuple<Func<object, object>, Action<object, object>>> LocalCache;

        public jsComplex() {
            var T = this.GetType();

            if (!Cache.TryGetValue(T, out LocalCache)) lock (Cache) {
                    Cache[T] = LocalCache = InitType(T);
                }
        }

        public override void CopyTo(jsObject Object) {
            base.CopyTo(Object);

            foreach (var Pair in LocalCache) {
                Object.AssignValue(Pair.Key, Pair.Value.Item1(this));
            }
        }

        public override bool TryGet(string Key, out object Value) {
            if (base.TryGet(Key, out Value))
                return true;

            if (LocalCache.ContainsKey(Key)) {
                Value = LocalCache[Key].Item1(this);
                return true;
            }

            Value = null;
            return false;
        }

        public override bool TryGet<T>(string Key, out T Value) {
            if (base.TryGet<T>(Key, out Value))
                return true;

            if (LocalCache.ContainsKey(Key)) {
                var Val = LocalCache[Key].Item1(this);

                if (Val is T) {
                    Value = (T)(Val);
                    return true;
                }
            }

            Value = default(T);
            return false;
        }

        public override void AssignValue<T>(string Key, T Value) {
            if (LocalCache.ContainsKey(Key)) {
                LocalCache[Key].Item2(this, Value);
            }
            else {
                base.AssignValue(Key, Value);
            }
        }
        
        public static StringBuilder Stringify(StringBuilder Output, jsComplex This, jsObject Parent) {
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
                    jsObject.Stringify(Output, Value as jsObject, This);
                }
                else if (Value is bool) {
                    Output.Append(Value.ToString().ToLower());
                }
                else {
                    Output.AppendFormat("\"{0}\"", Value.ToString().Escape());
                }

                if (Index != This.Count) {
                    Output.Append(",");
                    Index++;
                }
            }

            Index = 1;
            foreach (var Pair in This.LocalCache) {
                var Key = Pair.Key;
                var Value = Pair.Value.Item1(This);

                if (Object.ReferenceEquals(Value, Parent))
                    continue;

                if (!This.IsArray) {
                    Output.AppendFormat("\"{0}\":", Pair.Key);
                }

                if (Value is jsComplex) {
                    jsComplex.Stringify(Output, Value as jsComplex, This);
                }
                else 
                if (Value is jsObject) {
                    jsObject.Stringify(Output, Value as jsObject, This);
                }
                else 
                if (Value is bool) {
                    Output.Append(Value.ToString().ToLower());
                }
                else 
                if (Value != null) {
                    Output.AppendFormat("\"{0}\"", Value.ToString().Escape());
                }
                else continue;

                if (Index != This.LocalCache.Count) {
                    Output.Append(",");
                    Index++;
                }
            }

            Output.Append(This.IsArray ? "]" : "}");

            return Output;
        }

        public static StringBuilder Stringify(StringBuilder Output, jsComplex This, jsObject Parent, int Tabs) {
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
                    jsComplex.Stringify(Output, Value as jsComplex, This);
                }
                else if (Value is jsObject) {
                    jsObject.Stringify(Output, Value as jsObject, This);
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

                Output.Append(Environment.NewLine);
                Output.Append('\t', Tabs);
            }
            
            Index = 1;
            foreach (var Pair in This.LocalCache) {
                var Key = Pair.Key;
                var Value = Pair.Value.Item1(This);

                if (Object.ReferenceEquals(Value, Parent))
                    continue;

                if (!This.IsArray) {
                    Output.AppendFormat("\"{0}\":", Pair.Key);
                }

                if (Value is jsComplex) {
                    jsComplex.Stringify(Output, Value as jsComplex, This, Tabs + 1);
                }
                else if (Value is jsObject) {
                    jsObject.Stringify(Output, Value as jsObject, This, Tabs + 1);
                }
                else if (Value is bool) {
                    Output.Append(Value.ToString().ToLower());
                }
                else if (Value != null) {
                    Output.AppendFormat("\"{0}\"", Value.ToString().Escape());
                }
                else continue;

                if (Index != This.LocalCache.Count) {
                    Output.Append(",");
                    Index++;
                }

                Output.AppendLine().Append('\t', Tabs);
            }

            Output.Append(This.IsArray ? "]" : "}");

            return Output;
        }

        public static string Stringify(jsComplex This, bool HumanFormat) {
            if (HumanFormat) {
                return jsComplex.Stringify(new StringBuilder(), This, null, 1).ToString();
            }
            else {
                return jsComplex.Stringify(new StringBuilder(), This, null).ToString();
            }
        }

        public override string ToString() {
            return jsComplex.Stringify(this, false);
        }
    }
}
