using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Reload : Node {
        public Engine Engine = null;
        public Node Name = null;

        public Reload(Engine Engine, Node Name) {
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
            FileName = Path.GetFullPath(FileName);

            if (Engine.Includes.ContainsKey(FileName)) {
                return Engine.Includes[FileName].Reload();
            }

            return null;
        }

        public override string ToString() {
            return "reload '" + Name.ToString() + "'";
        }

        public static Node Parse(Engine Engine, StringIterator It) {
            if (It.Consume("reload")) {
                var Name = Engine.ParseValue(It);

                if (Name != null)
                    return new Reload(Engine, Name);
            }
            return null;
        }
    }
}
