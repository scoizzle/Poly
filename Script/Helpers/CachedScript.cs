using System;
using System.IO;

using Poly.Data;

namespace Poly.Script.Helpers {
    using Nodes;

    public class CachedScript : Node {
        Engine Engine;

        string FileName;
        DateTime LastWriteTime;

        public CachedScript(Engine Engine, string File, DateTime Time) {
            this.Engine = Engine;

            this.FileName = File;
            this.LastWriteTime = Time;
        }

        public override object Evaluate(jsObject Context) {
            if (Reload())
                return base.Evaluate(Context);

            return null;
        }

        public bool IsCurrent() {
            return File.Exists(FileName) ?
                File.GetLastWriteTime(FileName) == LastWriteTime :
                false;
        }

        public bool Reload() {
            if (this.IsCurrent())
                return true;

            this.Elements = null;

            return Engine.Parse(File.ReadAllText(FileName), 0, this) != null;
        }
    }
}
