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
                return Activator.CreateInstance(Type, MemberFunction.GetArguments(Context));
            }
            catch { return null; }
        }

        public static Initializer TryCreate(string TypeName) {
            try {
                var T = Type.GetType(TypeName);

                if (T == null)
                    return null;

                return new Initializer(T);
            }
            catch {
                return null;
            }
        }
    }
}
