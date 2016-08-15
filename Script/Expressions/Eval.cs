using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions {
    using Nodes;
    using Expressions;
    using Data;

    class Eval : Expression {
        public Node Node;

        public override object Evaluate(jsObject Context) {
            return Node?.Evaluate(Context);
        }

        public override string ToString() {
            return string.Format("({0})", string.Join<Node>("", Elements));
		}

		new public static bool Parse(Engine Engine, StringIterator It, Node Storage) {
			return false;
		}

		new public static Node Parse(Engine Engine, StringIterator It) {
            if (It.Consume('(')) {
                var Open = It.Index;

                if (It.Goto('(', ')')) {
                    var Node = Engine.ParseOperation(It.Clone(Open, It.Index));

                    if (Node != null) {
                        It.Consume(')');
                        return new Eval() { Node = Node };
                    }
                }
            }

            return null;
		}
    }
}
