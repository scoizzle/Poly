using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    using Poly.Script.Helpers;

    public class ScriptCache {
        public Dictionary<string, DateTime> LastWriteTimes = new Dictionary<string, DateTime>();
        public Dictionary<string, Script.Engine> CachedScripts = new Dictionary<string, Script.Engine>();

        public bool IsCurrent(string Name) {
            Script.Engine Engine;
            if (CachedScripts.TryGetValue(Name, out Engine)) {
                foreach (var Pair in Engine.Includes) {
                    if (!Pair.Value.IsCurrent())
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

        public Script.Engine Load(string EngineIncludePath, string Name) {
            if (!File.Exists(Name))
                return null;

            var Engine = new Script.Engine();

            Engine.IncludePath = EngineIncludePath;

            if (!Engine.Parse(File.ReadAllText(Name)))
                return null;
            
            LastWriteTimes[Name] = File.GetLastWriteTime(Name);
            CachedScripts[Name] = Engine;
            return Engine;
        }

        public Script.Engine Get(string EngineIncludePath, string Name) {
            if (IsCurrent(Name)) {
                return CachedScripts[Name];
            }
            return Load(EngineIncludePath, Name);
        }
    }
}
