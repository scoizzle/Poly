using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;
    using Expressions;
    public class For : Expression {
        public Node Init = null,
                    Boolean = null,
                    Modifier = null;

        public override object Evaluate(Data.jsObject Context) {
            if (Init != null)
                Init.Evaluate(Context);

            while (Bool.EvaluateNode(Boolean, Context)) {
                for (int i = 0; i < Elements.Length; i++) {
                    var Node = Elements[i];

                    if (Node is Return)
                        return Node;

                    var Result = Node.Evaluate(Context);

                    if (Result == Break)
                        return null;

                    if (Result == Continue)
                        break;
                }

                if (Modifier != null)
                    Modifier.Evaluate(Context);
            }

            return null;
        }

        public override string ToString() {
            return "for (" + Init.ToString() + "; " + Boolean.ToString() + "; " + Modifier.ToString() + ") " +
                base.ToString();
        }

        new public static Node Parse(Engine Engine, StringIterator It) {
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
                            var Var = Node.Init as Variable;
                            var Bol = Node.Boolean as Between;

                            Node.Modifier = new Assign(Var, new Add(Var, new StaticValue(1)));
                            Node.Init = new Assign(Var, Bol.Left);
                            Bol.Left = Var;

                        }
                        else {
                            Node.Modifier = Engine.ParseOperation(Sub);
                        }

                        It.Consume(')');
                        It.Consume(WhitespaceFuncs);

                        if (It.IsAt('{')) {
                            Expression.Parse(Engine, It, Node);
                        }
                        else {
                            Node.Elements = new Node[] {
                                Engine.ParseExpression (It)
                            };
                        }

                        return Node;
                    }
                }
            }

            return null;
        }
    }
}
