using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;

namespace Poly.Script.Node {
    public class Reload : Node {
        public Engine Engine = null;
        public object Name = null;

        public Reload(Engine Engine, object Name) {
            this.Engine = Engine;
            this.Name = Name;
        }

        public override object Evaluate(jsObject Context) {
            var FileName = GetValue(Name, Context).ToString();
            FileName = Path.GetFullPath(FileName);

            if (this.Engine.Includes.ContainsKey(FileName)) {
                return this.Engine.Includes[FileName].Reload(this.Engine);
            }

            return null;
        }

        public override string ToString() {
            return "reload '" + Name.ToString() + "'";
        }

        public static new object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("reload", Index)) {
                var Delta = Index += 6;
                Text.ConsumeWhitespace(ref Delta);

                var Inc = Engine.Parse(Text, ref Delta, LastIndex);

                Index = Delta;
                return new Reload(Engine, Inc);           
            }

            return null;
        }
    }
}
