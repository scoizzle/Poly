using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Types {
    using Nodes;
    public class Object : DataType<jsObject> {
        public new static object Add(jsObject Left,  object Right) {
            Left[Left.Count.ToString()] = Right;
            return Left;
        }

        public new static object Subtract(jsObject Left, object Right) {
            if (Right is String) {
                Left[Right.ToString()] = null;
            }
            return null;
        }

        public new static bool Equal(jsObject Left, object Right) {
            return Object.ReferenceEquals(Left, Right);
        }

        public class Builder : Value {
            private bool IsArray = false;
            private Dictionary<object, object> List = new Dictionary<object, object>();

            public Builder(Engine Eng, jsObject Obj) {
                this.Prepare(Eng, Obj);
                this.IsArray = Obj.IsArray;
            }

            private void Prepare(Engine Engine, jsObject Obj) {
                Obj.ForEach((K, V) => {
                    object Key, Value;
                    if (K.StartsWith("@")) {
                        Key = Variable.Parse(Engine, K, 1);
                    }
                    else {
                        Key = K;
                    }

                    if (V is jsObject) {
                        Value = new Builder(Engine, V as jsObject);
                    }
                    else if (V is string && (V as string).StartsWith("@")) {
                        Value = Variable.Parse(Engine, V as string, 1);
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
                    return new Builder(Engine, Obj);
                }
            }
            else if (Text[Delta] == '[') {
                var String = Text.FindMatchingBrackets("[", "]", Delta, true);

                var Obj = new jsObject() {
                    IsArray = true
                };

                if (jsObject.Parse(String, 0, Obj)) {
                    Index = Delta + String.Length;
                    return new Builder(Engine, Obj);
                }
            }

            return null;
        }
    }
}
