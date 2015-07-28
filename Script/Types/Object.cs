using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Types {
    using Nodes;
    public class ObjectBuilder : Value {
        private bool IsArray = false;
        private Dictionary<object, object> List = new Dictionary<object, object>();

        public ObjectBuilder(Engine Eng, jsObject Obj) {
            this.Prepare(Eng, Obj);
            this.IsArray = Obj.IsArray;
        }

        private void Prepare(Engine Engine, jsObject Obj) {
            Obj.ForEach((K, V) => {
                object Key, Value;
                if (K.StartsWith("@")) {
                    Key = Engine.Parse(K, 1);
                }
                else {
                    Key = K;
                }

                if (V is jsObject) {
                    Value = new ObjectBuilder(Engine, V as jsObject);
                }
                else if (V is string && (V as string).StartsWith("@")) {
                    Value = Engine.Parse(V as string, 1);
                }
                else {
                    Value = V;
                }

                List.Add(Key, Value);
            });
        }

        public override object Evaluate(jsObject Context) {
            jsObject Object = new jsObject() { IsArray = this.IsArray };

            foreach (var Pair in List) {
                object K;

                var N = Pair.Key as Node;

                if (N != null)
                    K = N.Evaluate(Context);
                else
                    K = Pair.Key;

                if (K == null)
                    K = Pair.Key;

                var Key = K.ToString();

                object V;
                N = Pair.Value as Node;

                if (N != null)
                    V = N.Evaluate(Context);
                else
                    V = Pair.Value;

                Object.Set(Key, V);
            }

            return Object;
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;

            if (Text[Delta] == '{') {
                var String = Text.FindMatchingBrackets("{", "}", Delta, true);
                var Obj = new jsObject();

                if (jsObject.Parse(String, 0, Obj)) {
                    Index = Delta + String.Length;
                    return new ObjectBuilder(Engine, Obj);
                }
            }
            else if (Text[Delta] == '[') {
                var String = Text.FindMatchingBrackets("[", "]", Delta, true);

                var Obj = new jsObject() {
                    IsArray = true
                };

                if (jsObject.Parse(String, 0, Obj)) {
                    Index = Delta + String.Length;
                    return new ObjectBuilder(Engine, Obj);
                }
            }

            return null;
        }
    }
}

