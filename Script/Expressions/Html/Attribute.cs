using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class Attribute : Element {
        public string Key;
        public Node Value;

        public Attribute() { }

        public Attribute(string Key, Node Value) {
            this.Key = Key;
            this.Value = Value;
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            Output.Append(" ").Append(Key);
            
            if (Value != null)
                Output.Append("=\"")
                      .Append(Value.Evaluate(Context))
                      .Append('"');
        }

        public override void ToEvaluationArray(StringBuilder output, List<Action<StringBuilder, jsObject>> list) {
            output.Append(" ").Append(Key);

            if (Value != null) {
                output.Append('"');

                if (Value is StaticValue)
                    output.Append(Value.ToString());
                else {
                    list.Add(StaticAppender(output));
                    list.Add(NodeAppender(Value.Evaluate));
                    output.Clear();
                }

                output.Append('"');
            }
        }

        new public static Element Parse(Engine Engine, StringIterator It) {
            var Start = It.Index;

            if (It.Consume(AttributeNameFuncs)) {
                var Name = It.Substring(Start, It.Index - Start);
                It.ConsumeWhitespace();

                if (It.Consume(':')) {
                    return new Attribute(Name, Engine.ParseOperation(It));
                }
                else It.Index = Start;
            }

            return null;
        }

        public override string ToString() {
            return string.Format("{0}=\"{1}\"", Key, Value);
        }
    }
}