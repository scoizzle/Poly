using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Nodes {
	using Data;

    public class Value : Node {
        public override object Evaluate(jsObject Context) {
            return this;
        }

        public virtual void Evaluate(StringBuilder Output, jsObject Context) {
            Output.Append(this);
        }
    }
}
