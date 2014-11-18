using System;
using Poly.Data;

namespace Poly.Script.Types {
    using Nodes;
    public class ClassInstance : jsObject {
        public Class Class;

        public ClassInstance(Class C) {
            this.Class = C;
        }
    }
}
