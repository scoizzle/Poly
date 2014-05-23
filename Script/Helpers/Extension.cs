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

        private static object IncludeMultiple(Engine Engine, string Name) {
            var Parts = Name.Split('\\', '/');

            if (Parts.Length == 0)
                return false;

            var Files = Directory.GetFiles(Parts[0], Name.Substring(Parts[0].Length + 1), SearchOption.TopDirectoryOnly);
            var Container = new Node.Node();

            foreach (var FileName in Files) {
                var Result = Include(Engine, FileName);

                if (Result != null) {
                    var jsRes = Result as Data.jsObject;
                    if (jsRes != null && !jsRes.IsEmpty) 
                        Container.Add(Result);
                }
                    
            }

            return Container;
        }

        public static object Include(Engine Engine, string FileName) {
            if (File.Exists(FileName) && !Engine.Includes.Contains(FileName)) {
                var Expression = Engine.Parse(File.ReadAllText(FileName), 0, new Node.Node()) as Node.Node;

                if (Expression != null) {
                    Engine.Includes.Add(FileName);

                    if (Expression.Count == 0)
                        return Node.Expression.NoOp;
                    else if (Expression.Count == 1) {
						return Expression.First().Value;
                    }
                    else {
                        return Expression;
                    }
                }
            }
            else if (FileName.Contains('*')) {
                return IncludeMultiple(Engine, FileName);
            }

            return null;
        }
    }
}
