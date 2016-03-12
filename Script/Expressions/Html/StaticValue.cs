using System;
using System.Text;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    using Data;

    public class StaticValue : Element {
        string Value;

        public override object Evaluate(jsObject Context) {
            return Value;
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            Output.Append(Value);
        }

        public override string ToString() {
            return Value;
        }

        new public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare('\'', Index)) {
                var String = Text.FindMatchingBrackets('\'', '\'', Index, false);

                Index += String.Length + 2;

                return new StaticValue() { Value = String.Descape() };
            }
            else
            if (Text.Compare('"', Index)) {
                var String = Text.FindMatchingBrackets('"', '"', Index, false);

                Index += String.Length + 2;

                return new StaticValue() { Value = String.Descape() };

            }
            else {
                var Next = Text.FirstPossibleIndex(Index, ',', '}');

                if (Next != -1) {
                    var String = Text.Substring(Index, Next - Index);
                    Index = Next + 1;

                    return new StaticValue() { Value = String };
                }
            }
            return null;
        }
    }
}
