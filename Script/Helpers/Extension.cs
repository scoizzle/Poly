using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace Poly.Script.Helper {
    public class ExtensionManager {
        public static void Load(string Name) {
            var Asm = Assembly.Load(Name);

            foreach (var Type in Asm.GetTypes()) {
                if (typeof(Library).IsAssignableFrom(Type)) {
                    Activator.CreateInstance(Type);
                }
            }
        }
    }
}
