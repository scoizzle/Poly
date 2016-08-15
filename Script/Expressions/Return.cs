using System;

namespace Poly.Script.Expressions {
    using Nodes;

    class Return : Expression {
        public Node Value = null;

        public override object Evaluate(Data.jsObject Context) {
            if (Value != null)
                return Value.Evaluate(Context);

            return null;
        }

        new public static Node Parse(Engine Engine, StringIterator It) {
            if (It.Consume("return")) {
                It.ConsumeWhitespace();

                if (It.IsAt(';'))
                    return new Return();

                var Val = Engine.ParseOperation(It);

                if (Val != null)
                    return new Return() { Value = Val };
            }

            return null;
        }
        
        public override string ToString() {
            if (Value != null)
                return "return " + Value.ToString();

            return "return";
        }
    }
}
