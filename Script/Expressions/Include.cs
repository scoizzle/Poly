using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Helpers;

    public class Include : Node {
        public Engine Engine = null;
        public Node Name = null;

        public Include(Engine Engine, Node Name) {
            this.Engine = Engine;
            this.Name = Name;
        }

        public override object Evaluate(jsObject Context) {
            if (Name == null)
                return null;

            var Value = Name.Evaluate(Context);

            if (Value == null)
                return null;

            var FileName = Value.ToString();
            var Obj = ExtensionManager.Include(Engine, FileName);

            if (Obj != null)
                return Obj.Evaluate(Context);

            return null;
        }

        public override string ToString() {
            return "include '" + Name.ToString() + "'";
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("include", Index)) {
                var Delta = Index += 7;
                bool Live = false;

                if (Text.Compare("_live", Delta)) {
                    Delta += 5;
                    Live = true;
                }
                ConsumeWhitespace(Text, ref Delta);

                var Inc = Engine.Parse(Text, ref Delta, LastIndex);

                Index = Delta;
                if (Inc is Types.String && !Live) {
                    return ExtensionManager.Include(Engine, Engine.IncludePath + Inc.ToString());
                }
                else {
                    return new Include(Engine, Inc);
                }                  
            }

            return null;
        }
    }
}
