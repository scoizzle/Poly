using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;
using Poly.Script.Node;

namespace Poly.Script {
    public partial class Engine : Expression {
        public Library Functions = new Library();
        public jsObject StaticObjects = new jsObject();

        public List<Library> Using = new List<Library>();
        public List<string> Includes = new List<string>();

        public jsObject<CustomType> Types = new jsObject<CustomType>();
        public jsObject<Helper.MemberFunction> RawFunctionCache = new jsObject<Helper.MemberFunction>();
        public jsObject<Helper.Initializer> RawInitializerCache = new jsObject<Helper.Initializer>();

        public Engine() {
            Using.Add(Library.Defined["Standard"]);
        }

        public object Evaluate(string Script, params object[] Args) {
            var Context = new jsObject(Args);

			if (Parse (Script)) {
				return Evaluate (Context);
			}

            return null;
        }

		public static object Eval(string Script, jsObject Context = null) {
			var Eng = new Engine ();

			if (Eng.Parse (Script)) {
				if (Context == null) {
					Context = new jsObject ();
				}

				return Eng.Evaluate (Context);
			}

			return null;
		}
    }
}
