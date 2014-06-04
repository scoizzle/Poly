using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    public class ScriptCache {
        public Dictionary<string, DateTime> LastWriteTimes = new Dictionary<string, DateTime>();
        public Dictionary<string, Script.Engine> CachedScripts = new Dictionary<string, Script.Engine>();

        public bool IsCurrent(string Name) {
            Script.Engine Engine;
            if (CachedScripts.TryGetValue(Name, out Engine)) {
                foreach (var Pair in Engine.Includes) {
                    var Cached = Pair.Value as Script.Helper.CachedScript;

                    if (!Cached.IsCurrent())
                        return false;
                }
            }

            DateTime Time;
            if (LastWriteTimes.TryGetValue(Name, out Time)) {
                if (Time != File.GetLastWriteTime(Name)) {
                    return false;
                }

                return true;
            }

            return false;
        }

        public Script.Engine Load(string Name) {
            if (!File.Exists(Name))
                return null;

            var Engine = new Script.Engine();

            if (!Engine.Parse(File.ReadAllText(Name)))
                return null;
            
            LastWriteTimes[Name] = File.GetLastWriteTime(Name);
            CachedScripts[Name] = Engine;
            return Engine;
        }

        public Script.Engine Get(string Name) {
            if (IsCurrent(Name)) {
                return CachedScripts[Name];
            }
            return Load(Name);
        }
    }
}
