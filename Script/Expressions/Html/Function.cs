using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;

    public class Function : Nodes.Function {
        public Element Format;

        public Function(string Name)
            : base(Name) {

        }

        public override object Evaluate(jsObject Context) {
            if (Format != null)
                return Format.Evaluate(Context);

            return null;
        }

        public virtual void Evaluate(StringBuilder Output, jsObject Context) {
            if (Format != null)
                Format.Evaluate(Output, Context);
        }
    }
}
