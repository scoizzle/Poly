using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;

namespace Poly.Script.Node {
    public class Include : Node {
        public Engine Engine = null;
        public object Name = null;

        public Include(Engine Engine, object Name) {
            this.Engine = Engine;
            this.Name = Name;
        }

        public override object Evaluate(jsObject Context) {
            var FileName = GetValue(Name, Context).ToString();
            var Obj = Helper.ExtensionManager.Include(Engine, FileName);

            return GetValue(Obj, Context);
        }

        public override string ToString() {
            return "include '" + Name.ToString() + "'";
        }

        public static new object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
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
                if (Inc is string && !Live) {
                    return Helper.ExtensionManager.Include(Engine, Inc as string);
                }
                else {
                    return new Include(Engine, Inc);
                }                  
            }

            return null;
        }
    }
}
