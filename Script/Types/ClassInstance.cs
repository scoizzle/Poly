using System;
using Poly.Data;

namespace Poly.Script.Types {
    using Nodes;
    public class ClassInstance : jsObject {
        public Class Class;

        public ClassInstance(Class C) {
            this.Class = C;
        }

        public Function GetFunction(string Name) {
            return Class.GetFunction(Name);
        }

        public Function GetStaticFunction(string Name) {
            return Class.StaticFunctions[Name];
        }
    }
}
