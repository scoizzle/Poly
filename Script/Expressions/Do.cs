using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;
    using Expressions;

    public class Do : Expression {
        public Node Boolean = null;

        public override object Evaluate(Data.jsObject Context) {
            do {
                if (Elements != null)
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
			while (Bool.EvaluateNode(Boolean, Context));

            return null;
        }

        public override string ToString() {
            return "do {" + base.ToString() + " } while (" + Boolean.ToString() + ");";
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("do")) {
				It.Consume (WhitespaceFuncs);

				var Node = new Do ();

				if (It.IsAt ('{')) {
					Expression.Parse (Engine, It, Node);
				}
				else {
					Node.Elements = new Node[] {
						Engine.ParseExpression (It)
					};
				}
				It.Consume (WhitespaceFuncs);

				if (It.Consume ("while")) {
					It.Consume (WhitespaceFuncs);

					var Open = It.Index;  

					if (It.Consume ('(') && It.Goto ('(', ')')) {
						Node.Boolean = Engine.ParseValue (It.Clone (Open + 1, It.Index));

						if (It.Consume (')'))
							return Node;
					}
				}

			}
			return null;
		}
    }
}
