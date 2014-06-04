using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;

namespace Poly.Script.Helper {
    using Node;

    public class CachedScript {
        public string FileName;
        public DateTime LastWriteTime;
        public Node Parsed;

        public CachedScript(string File, DateTime Time, Node Parsed) {
            this.FileName = File;
            this.LastWriteTime = Time;
            this.Parsed = Parsed;
        }

        public bool IsCurrent() {
            return File.Exists(FileName) ? 
                File.GetLastWriteTime(FileName) == LastWriteTime :
                false;
        }

        public bool Reload(Engine Engine) {
            if (this.IsCurrent())
                return true;

            Parsed.Clear();

            return Engine.Parse(File.ReadAllText(FileName), 0, Parsed) != null;
        }
    }

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
            var Container = new Node();

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
            if (File.Exists(FileName)) {
                FileName = Path.GetFullPath(FileName);
                DateTime LastWrite = File.GetLastWriteTime(FileName);

                CachedScript Inc = null;
                if (Engine.Includes.TryGetValue(FileName, out Inc)) {
                    if (Inc.LastWriteTime == LastWrite) {
                        return Inc.Parsed;
                    }
                    else {
                        Inc.Parsed.Clear();
                    }
                }
                else {
                    Inc = new CachedScript(FileName, LastWrite, new Node());

                }

                if (Engine.Parse(File.ReadAllText(FileName), 0, Inc.Parsed) != null) {
                    Engine.Includes.Add(FileName, Inc);
                    return Inc.Parsed;
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
                return Engine.Includes[FileName].Reload(Engine);
            }

            return false;
        }
    }
}
