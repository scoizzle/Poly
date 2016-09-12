using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Threading;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class While : Expression {
        public Node Boolean = null;

        public override object Evaluate(Data.jsObject Context) {
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
            }            

            return null;
        }

        public override string ToString() {
            return "while (" + Boolean.ToString() + ") " + base.ToString();
		}

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("while")) {
                It.ConsumeWhitespace();

                var Node = new While() {
                    Boolean = Eval.Parse(Engine, It)
                };
                
                if (Node.Boolean != null) {
                    It.ConsumeWhitespace();

					if (It.IsAt ('{')) {
						Expression.Parse (Engine, It, Node);
					}
					else {
						Node.Elements = new Node[] {
							Engine.ParseExpression (It)
						};
					}

					return Node;
				}
			}
			return null;
		}
    }
}
