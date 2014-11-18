using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;

namespace Poly.Script.Helpers {
    using Nodes;

    public class ExtensionManager {
        public static void Load(string Name) {
            var Asm = Assembly.Load(Name);

            foreach (var Type in Asm.GetTypes()) {
                if (typeof(Library).IsAssignableFrom(Type)) {
                    Activator.CreateInstance(Type);
                }
            }
        }

        private static Node IncludeMultiple(Engine Engine, string Name) {
            var Parts = Name.Split('\\', '/');

            if (Parts.Length == 0)
                return null;

            var Files = Directory.GetFiles(Parts[0], Name.Substring(Parts[0].Length + 1), SearchOption.TopDirectoryOnly);
            var List = new List<Node>();

            foreach (var FileName in Files) {
                var Result = Include(Engine, FileName);

                if (Result != null) {
                    var jsRes = Result as Node;
                    if (jsRes != null) 
                        List.Add(Result);
                }
                    
            }

            return new Node() { Elements = List.ToArray() };
        }

        public static Node Include(Engine Engine, string FileName) {
            if (File.Exists(FileName)) {
                FileName = Path.GetFullPath(FileName);
                DateTime LastWrite = File.GetLastWriteTime(FileName);

                CachedScript Inc = null;
                if (Engine.Includes.TryGetValue(FileName, out Inc) && Inc.IsCurrent()) {
                        return Inc;
                }
                else {
                    Inc = new CachedScript(Engine, FileName, LastWrite);
                }

                if (Engine.Parse(File.ReadAllText(FileName), 0, Inc) != null) {
                    Engine.Includes.Add(FileName, Inc);
                    return Inc;
                }
            }
            else if (FileName.Contains('*')) {
                return IncludeMultiple(Engine, FileName);
            }

            return null;
        }

        public static bool Reload(Engine Engine, string FileName) {
            FileName = Path.GetFullPath(FileName);

            if (Engine.Includes.ContainsKey(FileName)) {
                return Engine.Includes[FileName].Reload();
            }

            return false;
        }
    }
}
