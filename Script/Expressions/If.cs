using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class If : Expression {
        public Node Boolean = null, Else = null;

        public override object Evaluate(jsObject Context) {
            if (Bool.EvaluateNode(Boolean, Context)) {
                return base.Evaluate(Context);
            }
            else if (Else != null) {
                return Else.Evaluate(Context);
            }
            return null;
        }

        public override string ToString() {
            return "if (" + Boolean.ToString() + ")" + base.ToString() + 
                ((Else != null) ?  Else.ToString() : "");
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("if")) {
				It.Consume (WhitespaceFuncs);

				var Node = new If () {
					Boolean = Eval.Parse(Engine, It)
				};

				if (Node.Boolean != null) {
					It.Tick ();
					It.Consume (WhitespaceFuncs); 

					if (It.IsAt ('{')) {
						Expression.Parse (Engine, It, Node);
					}
					else {
						Node.Elements = new Node[] {
							Engine.ParseExpression (It)
						};
					}

					It.Consume (WhitespaceFuncs);

					if (It.Consume ("else")) {
						It.Consume (WhitespaceFuncs);

                        if (It.IsAt('{')) {
                            Node.Else = Expression.Parse(Engine, It);
                        }
                        else {
                            Node.Else = Engine.ParseExpression(It);
                        }
					}

					return Node;

				}
			}
			return null;
		}
    }
}
