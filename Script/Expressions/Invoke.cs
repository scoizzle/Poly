using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Invoke : Expression {
        public Node Handler;

		public Invoke(Node FunctionRef) {
			Handler = FunctionRef;
		}

        public override object Evaluate(jsObject Context) {
            var Func = Handler.Evaluate(Context);

            if (Func is Event.Handler) {
                return (Func as Event.Handler)(Context);
            }

            return null;
        }

        public override string ToString() {
            return "@" + Handler.ToString();
        }

		new public static Invoke Parse(Engine Engine, StringIterator It) {
			if (It.Consume ('@')) {
				var FunctionRef = Engine.Parse (It);

				if (FunctionRef != null)
					return new Invoke (FunctionRef);
			}
			return null;
		}        
    }
}
