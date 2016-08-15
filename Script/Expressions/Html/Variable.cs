using System;
using System.Text;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class Variable : Element {
        public Node Value;

        public Variable() { }

        public Variable(Node Val) {
            Value = Val;
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            var Result = Value?.Evaluate(Context);

            if (Result != null)
                Output.Append(Result);
        }

		new public static Element Parse(Engine Engine, StringIterator It) {
			var Var = Nodes.Variable.Parse (Engine, It);

			if (Var != null) {
				return new Variable (Var);
			}
			return null;
		}
    }
}