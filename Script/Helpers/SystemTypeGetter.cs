using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Helpers {
    using Nodes;
    public class SystemTypeGetter : Node {
        public Type Cache;
        public string Name;

        public SystemTypeGetter(string Name) {
            this.Name = Name;
        }

        public override object Evaluate(Data.jsObject Context) {
            if (Cache != null)
                return Cache;

            Cache = GetType(Name);
            return Cache;
        }

        public static Type GetType(string Name) {
            foreach (var Mod in AppDomain.CurrentDomain.GetAssemblies()) {
                var T = Mod.GetType(Name, false);

                if (T != null)
                    return T;
            }
            return null;
        }

        public override string ToString() {
            return Name;
        }
    }
}
