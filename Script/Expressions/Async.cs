using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;

    public class Async : Expression {
        public Node Node = null;

        public override object Evaluate(jsObject Context) {
            return Task.Factory.StartNew(() => {
                var Result = Node.Evaluate(Context);

                if (Result is Return) {
                    Result = (Result as Return).Evaluate(Context);
                }

                return Result;
            });
        }

        public override string ToString() {
            return "async " + base.ToString();
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("async")) {
				var Node = new Async () {
					Node = Engine.ParseExpression(It)
				};

				return Node;
			}

			return null;
		}
    }
}
