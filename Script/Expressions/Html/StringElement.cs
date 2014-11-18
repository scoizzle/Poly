using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    public class StringElement : Element {
        public string Value;

        public StringElement(string Str) {
            this.Value = Str;
        }

        public override void Evaluate(StringBuilder Output, Data.jsObject Context) {
            Output.Append(this.Value);
        }

        public override string ToString() {
            return this.Value;
        }
    }
}
