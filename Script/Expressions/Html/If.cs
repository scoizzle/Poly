using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;
    using Types;

    public class If : Element {
        public Node Boolean;
        public Element Else;

        public If(Node Bool) {
            Boolean = Bool;
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            if (Bool.EvaluateNode(Boolean, Context)) {
                base.Evaluate(Output, Context);
            }
            else {
                Else?.Evaluate(Output, Context);
            }
        }

        new public static Element Parse(Engine Engine, StringIterator It) {
            var Begin = It.Index;
            if (It.Consume("if")) { 
                It.Consume(WhitespaceFuncs);

                var Node = new If(Eval.Parse(Engine, It));

                if (Node.Boolean != null) {
                    It.Tick();
                    It.Consume(WhitespaceFuncs);

                    if (It.IsAt('{')) {
                        Document.Parse(Engine, It, Node);
                    }
                    else {
                        Node.Elements = new Node[] {
                            Html.ParseElement(Engine, It)
                        };
                    }

                    It.Consume(WhitespaceFuncs);

                    if (It.Consume("else")) {
                        It.Consume(WhitespaceFuncs);

                        Node.Else = Html.ParseElement(Engine, It);
                    }

                    return Node;

                }
            }

            It.Index = Begin;
            return null;
        }
    }
}