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
                        var TypeCache = new Dictionary<string, Tuple<Func<object, object>, Action<object, object>>>();

                        foreach (var Field in T.GetFields()) {
                            TypeCache.Add(Field.Name, new Tuple<Func<object, object>, Action<object, object>>(
                                Field.GetValue,
                                Field.SetValue
                            ));
                        }
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

        public override bool GetValue(string Key, out object Value) {
            if (TryGetValue(Key, out Value))
                return true;

            if (LocalCache.ContainsKey(Key)) {
                Value = LocalCache[Key].Item1(this);
                return true;
            }

            Value = null;
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

        public static string Stringify(jsComplex This, bool HumanFormat) {
            return Stringify(new StringBuilder(), This, HumanFormat, 1);
        }

        public static string Stringify(StringBuilder Output, jsComplex This, bool HumanFormat, int Tabs) {
            Output.Append(This.IsArray ? '[' : '{');

            if (HumanFormat) {
                Output.AppendLine();
                Output.Append('\t', Tabs);
            }

            int Index = 0, Total = This.Count + This.LocalCache.Count;

            foreach (var Pair in This) {
                Index++;
                StringifyInternal(Output, This, Pair.Key, Pair.Value, Index == Total, HumanFormat, Tabs);
            }

            foreach (var Pair in This.LocalCache) {
                Index++;
                StringifyInternal(Output, This, Pair.Key, Pair.Value.Item1(This), Index == Total, HumanFormat, Tabs);
            }

            Output.Append(This.IsArray ? ']' : '}');

            if (Tabs > 1)
                return string.Empty;

            return Output.ToString();
        }

        public override string ToString() {
            return Stringify(this, false);
        }
    }
}
