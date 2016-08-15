using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;
    using Types;

    public class Foreach : Element {
        public Node List;
        public Nodes.Variable Variable, ValueVar;

        public Foreach() {
            List = null;
            Variable = null;
            ValueVar = null;
        }

        private void LoopNodes<K, V>(StringBuilder Output, jsObject Context, K Key, V Value) {
            var Var = new jsObject();
            Variable.Assign(Context, Var);

            Var.Set("Key", Key);
            Var.Set("Value", Value);

            foreach (Element Node in Elements) {
                Node?.Evaluate(Output, Context);
            }

            Variable.Assign(Context, null);
        }

        private void LoopNodes<K, V>(StringBuilder Output, jsObject Context, Nodes.Variable KeyName, K Key, Nodes.Variable ValueName, V Value) {
            KeyName?.Assign(Context, Key);
            ValueName.Assign(Context, Value);

            foreach (Element Node in Elements) {
                Node?.Evaluate(Output, Context);
            }
        }

        private void LoopString(StringBuilder Output, jsObject Context, string String) {
            if (string.IsNullOrEmpty(String))
                return;

            for (int i = 0; i < String.Length; i++) {
                if (ValueVar == null)
                    LoopNodes(Output, Context, i, String[i]);
                else
                    LoopNodes(Output, Context, Variable, i, ValueVar, String[i]);
            }
        }

        private void LoopObject(StringBuilder Output, jsObject Context, jsObject Object) {
            if (Object == null || Object.IsEmpty)
                return;

            foreach (var Pair in Object) {
                if (ValueVar == null)
                    LoopNodes(Output, Context, Pair.Key, Pair.Value);
                else
                    LoopNodes(Output, Context, Variable, Pair.Key, ValueVar, Pair.Value);
            }
        }

        private void LoopArray(StringBuilder Output, jsObject Context, Array Array) {
            for (int Index = 0; Index < Array.Length; Index++) {
                if (ValueVar == null)
                    LoopNodes(Output, Context, Index, Array.GetValue(Index));
                else
                    LoopNodes(Output, Context, Variable, Index, ValueVar, Array.GetValue(Index));
            }
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            var Collection = List?.Evaluate(Context);

            if (Collection == null)
                return;
            
            if (Collection is jsObject)
                LoopObject(Output, Context, Collection as jsObject);
            else if (Collection is string)
                LoopString(Output, Context, Collection as string);
            else if (Collection is Array)
                LoopArray(Output, Context, Collection as Array);
        }

        public override string ToString() {
            return "foreach (" + Variable?.ToString() + " in " + List.ToString() + ")" +
                base.ToString();
        }

        new public static Element Parse(Engine Engine, StringIterator It) {
            var Begin = It.Index;
            if (It.Consume("foreach")) { 
                It.Consume(WhitespaceFuncs);

                if (It.Consume('(')) {
                    var Node = new Foreach();
                    var Key = Engine.ParseOperation(It);

                    if (Key is KeyValuePair) {
                        var Pair = (Key as KeyValuePair);
                        Node.Variable = Pair.Left as Nodes.Variable;
                        Node.ValueVar = Pair.Right as Nodes.Variable;
                    }
                    else if (Key is Variable) {
                        Node.Variable = Key as Nodes.Variable;
                    }
                    else return null;

                    It.Consume(WhitespaceFuncs);
                    if (It.Consume("in")) {
                        It.Consume(WhitespaceFuncs);

                        Node.List = Engine.ParseValue(It);

                        It.Consume(WhitespaceFuncs);
                        if (It.Consume(')')) {
                            It.Consume(WhitespaceFuncs);

                            if (It.IsAt('{')) {
                                Document.Parse(Engine, It, Node);
                            }
                            else {
                                Node.Elements = new Node[] {
                                    Html.ParseElement(Engine, It)
                                };
                            }

                            return Node;
                        }
                    }
                }
            }
            It.Index = Begin;
            return null;
        }
    }
}