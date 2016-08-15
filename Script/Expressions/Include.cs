using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Helpers;

    public class Include : Expression {
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

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("include")) {
				bool Live = false;

				if (It.Consume ("_live"))
					Live = true;

				It.ConsumeWhitespace ();

				var Name = Engine.ParseValue (It);
                
				if (Live)
					return new Include (Engine, Name);
				else
					return ExtensionManager.Include (Engine, Engine.IncludePath + Name.ToString());
			}
			return null;
		}
	}
}
