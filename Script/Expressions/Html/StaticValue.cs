using System;
using System.Text;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    using Data;
    using System.Collections.Generic;

    public class StaticValue : Element {
        string Value;

        public StaticValue() {}

        public StaticValue(string Str) {
            Value = Str.Descape();
        }

        public override object Evaluate(jsObject Context) {
            return Value;
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            Output.Append(Value);
        }

        public override string ToString() {
            return Value;
        }

        public override void ToEvaluationArray(StringBuilder output, List<Action<StringBuilder, jsObject>> list) {
            output.Append(Value);
        }

        new public static Element Parse(Engine Engine, StringIterator It) {
            if (It.IsAt('"')) {
                return new StaticValue(It.Extract('"', '"'));
            }
            else if (It.IsAt('\'')) {
                return new StaticValue(It.Extract('\'', '\''));
            }

            return null;
        }
    }
}
