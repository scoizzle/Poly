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
			if (!IsCurrent ())
			if (!Reload ())
				return null;
			
            return base.Evaluate(Context);
        }

        public bool IsCurrent() {
            return File.Exists(FileName) ?
                File.GetLastWriteTime(FileName) == LastWriteTime :
                false;
        }

        public bool Reload() {
            this.Elements = null;

			var Result = Engine.Parse (File.ReadAllText (FileName), 0, this);

			if (Result != null) {
				if (Result is Value) {
					this.Elements = new Node[1] { Result };
				} else {
					this.Elements = Result.Elements;
				}

				return true;
			}

			return false;
        }

		public override string ToString () {
			return string.Format ("Cached {0} : {1}", FileName, LastWriteTime);
		}
    }
}
