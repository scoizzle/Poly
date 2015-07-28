using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Helpers {
    class ContextAccessor : Nodes.Node {
        public override object Evaluate(Data.jsObject Context) {
            return Context;
        }
    }
}
