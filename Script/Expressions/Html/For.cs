using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;
    using Types;

    public class For : Element {
        public Node Init = null,
                    Boolean = null,
                    Modifier = null;
        
        public override void Evaluate(StringBuilder Output, jsObject Context) {
            Init?.Evaluate(Context);

            while (Bool.EvaluateNode(Boolean, Context)) {
                foreach (Element e in Elements) {
                    e?.Evaluate(Output, Context);
                    Modifier?.Evaluate(Context);
                }
            }
        }

        public override string ToString() {
            return "for (" + Init.ToString() + "; " + Boolean.ToString() + "; " + Modifier.ToString() + ") " +
                base.ToString();
        }

        new public static Element Parse(Engine Engine, StringIterator It) {
            var Begin = It.Index;
            if (It.Consume("for")) { 
                It.Consume(WhitespaceFuncs);

                if (It.Consume('(')) {
                    var Start = It.Index;

                    if (It.Goto('(', ')')) {
                        var Sub = It.Clone(Start, It.Index);

                        var Node = new For() {
                            Init = Engine.ParseOperation(Sub),
                            Boolean = Engine.ParseOperation(Sub)
                        };

                        if (Node.Boolean is Between) {
                            var Var = Node.Init as Nodes.Variable;
                            var Bol = Node.Boolean as Between;

                            Node.Modifier = new Assign(Var, new Add(Var, new Nodes.StaticValue(1)));
                            Node.Init = new Assign(Var, Bol.Left);
                            Bol.Left = Var;

                        }
                        else {
                            Node.Modifier = Engine.ParseOperation(Sub);
                        }

                        It.Consume(')');
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

            It.Index = Begin;
            return null;
        }
    }
}