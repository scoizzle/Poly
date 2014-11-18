using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Helpers {
    using Nodes;
    public class ContextGetter : Node {
        public override object Evaluate(Data.jsObject Context) {
            return Context;
        }
    }
}
