using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Data;
    using Types;
    public class KeyValuePair : Operator {
        public KeyValuePair(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

		public override object Evaluate (jsObject Context)
		{
			return Eval (Context);
		}

		public KeyValuePair<object, object> Eval(jsObject Context) {
			return new KeyValuePair<object, object>(
				Left?.Evaluate(Context) ?? Left.ToString(), 
				Right?.Evaluate(Context)
			);
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            Output.Append(Left?.Evaluate(Context) ?? Left)
                .Append("=\"")
                .Append(Right?.Evaluate(Context))
                .Append('"');
        }

        public static Operator Parse(Engine Engine, StringIterator It) {
			var Left = Engine.ParseValue (It);
			It.Consume (WhitespaceFuncs);

			if (It.Consume (':')) {
				var Right = Engine.ParseValue (It);

				return new KeyValuePair (Left, Right);
			}
			return null;
		}

        public static Operator ParseValue(Engine Engine, StringIterator It) {
            if (It.Consume(':')) {
                var Right = Engine.ParseValue(It);

                return new KeyValuePair(null, Right);
            }
            return null;
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new KeyValuePair(Left, Engine.ParseValue(It));
        }

        public override string ToString() {
            return Left.ToString() + ": " + Right.ToString();
        }
    }
}
