using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime;
using System.Reflection;

namespace Poly.Script.Helpers {
    using Nodes;
    public class SystemTypeGetter : Node {
        public Type Cache;
        public string Name;

        public SystemTypeGetter(string Name) {
            this.Name = Name;
        }

        public SystemTypeGetter(Type Type) {
            Cache = Type;
        }

        public override object Evaluate(Data.jsObject Context) {
            return Cache ?? (Cache = GetType(Name));
        }

        public static Type GetType(string Name) {
            //var Asm = Assembly.GetEntryAssembly().

            //foreach (var Type in Asm.GetTypes()) {
            //    foreach (var Mod in AppDomain.CurrentDomain.GetAssemblies()) {
            //    var T = Mod.GetType(Name, false);

            //    if (T != null)
            //        return T;
            //}
            return null;
        }

        public override string ToString() {
            return Name;
        }
    }
}
