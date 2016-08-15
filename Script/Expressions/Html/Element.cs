using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class Element : Expression {
        public static Func<char, bool>[] ElementNameFuncs,
                                         ComplexNameFuncs,
                                         AttributeNameFuncs;

        static Element() {
            ElementNameFuncs = new Func<char, bool>[] {
                char.IsLetterOrDigit,
                c => c == '!' || c == '-'
            };

            ComplexNameFuncs = new Func<char, bool>[] {
                char.IsLetterOrDigit,
                char.IsWhiteSpace,
                c => c == '.' || c == '!' || c == '-' || c == '#'
            };

            AttributeNameFuncs = new Func<char, bool>[] {
                char.IsLetterOrDigit,
                c => c == '-'
            };
        }

        public virtual void Evaluate(StringBuilder Output, jsObject Context) {
            foreach (Element e in Elements) {
                e.Evaluate(Output, Context);
            }
        }

        public override object Evaluate(jsObject Context) {
            var Output = new StringBuilder();
            Evaluate(Output, Context);
            return Output.ToString();
        }

        public virtual void ToEvaluationArray(StringBuilder output, List<Action<StringBuilder, jsObject>> list) {
            if (output.Length > 0) {
                list.Add(StaticAppender(output));
                output.Clear();
            }

            list.Add(Evaluate);
        }

        public static Action<StringBuilder, jsObject> NodeAppender(Event.Handler Handle) {
            return (o, c) => {
                o.Append(Handle(c));
            };
        }

        public static Action<StringBuilder, jsObject> StaticAppender(StringBuilder Output) {
            var result = Output.ToString();

            return (o, c) => {
                o.Append(result);
            };
        }
    }
}
