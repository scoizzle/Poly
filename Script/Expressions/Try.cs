using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Try : Expression {
        public Node Node = null, Catch = null;

        public override object Evaluate(Data.jsObject Context) {
            try {
				return Node?.Evaluate(Context);
            }
            catch (Exception Error) {
                if (Catch != null) {
                    Context.Set("Error", Error);

                    if (Catch != null)
                        return Catch.Evaluate(Context);
                }
            }
            return null;
        }

        public override string ToString() {
            return "try " + base.ToString();
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("try")) {
				var Node = new Try ();

				Node.Node = Engine.ParseExpression (It);
				It.Consume (WhitespaceFuncs);

				if (It.Consume ("catch")) {
					Node.Catch = Engine.ParseExpression (It);
				}

				return Node;
			}
			return null;
		}
    }
}
