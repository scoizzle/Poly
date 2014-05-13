using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;

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

        public static object Include(Engine Engine, string FileName) {
            if (File.Exists(FileName)) {
                var Expression = Engine.Parse(File.ReadAllText(FileName), 0, new Node.Node()) as Node.Node;

                if (Expression != null) {
                    Engine.Includes.Add(FileName);

                    if (Expression.Count == 1) {
						return Expression.First().Value;
                    }
                    else {
                        return Expression;
                    }
                }
            }

            return null;
        }
    }
}
