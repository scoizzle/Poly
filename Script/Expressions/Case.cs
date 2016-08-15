using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Case : Expression {
        public Node Object = null;
        public bool IsDefault = false;

        public override string ToString() {
            return "case " + Object.ToString() + ":" + base.ToString();
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			Case Node;
			if (It.Consume ("case")) {
				Node = new Case ();
			} else if (It.Consume ("default")) {
				Node = new Case () { IsDefault = true };
			} else return null;

			It.Consume (WhitespaceFuncs);
			if (!Node.IsDefault) {
				Node.Object = Engine.ParseValue (It);
			}

			if (It.Consume (':')) {
				It.Consume (WhitespaceFuncs);

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
			return null;
		}
    }
}
