using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Helper {
    using Data;
    using Node;

    public class Initializer : Function {
        public Type Type = null;

        public Initializer(Type Type) {
            if (Type != null) {
                this.Name = Type.Name;
                this.Type = Type;
            }
        }

        public override object Evaluate(jsObject Context) {
            try {
                return Activator.CreateInstance(Type, SystemFunctions.GetArguments(Context));
            }
            catch { return null; }
        }

        public static Initializer Get(string Name) {
            var T = Helper.SystemFunctions.SearchForType(Name);

            if (T != null)
                return new Initializer(T);

            return null;
        }
    }
}
