using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;

    public class Await : Expression {
        public Node Node = null;

        public override object Evaluate(jsObject Context) {
            var Val = Node.Evaluate(Context) as Task<object>;

            if (Val != null) {
                Val.Wait();

                return Val.Result;
            }

            return null;
        }

        public override string ToString() {
            return "await " + Node.ToString();
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("await")) {
				var Node = new Await () {
					Node = Engine.ParseExpression(It)
				};

				return Node;
			}

			return null;
		}
    }
}
