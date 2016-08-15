using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    using Data;

    public class Optimizer : Node {
        public Action<StringBuilder, jsObject>[] Handlers;

        public Optimizer(Action<StringBuilder, jsObject>[] handlers) {
            Handlers = handlers;
        }

        public override object Evaluate(jsObject Context) {
            StringBuilder Output = new StringBuilder();
            foreach (var f in Handlers) {
                f(Output, Context);
            }
            return Output;
        }

        public static Optimizer FromDocument(Document Doc) {
            StringBuilder Output = new StringBuilder();
            List<Action<StringBuilder, jsObject>> List = new List<Action<StringBuilder, Data.jsObject>>();

            Doc.ToEvaluationArray(Output, List);

            return new Optimizer(List.ToArray());
        }
    }
}
