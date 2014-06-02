using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;
using Poly.Script.Node;

namespace Poly.Script {
    public partial class Engine : Expression {
        public Library Functions = new Library();
        public jsObject Static = new jsObject();

        public List<Library> Using = new List<Library>();
        public List<string> Includes = new List<string>();
        public Dictionary<string, string> Shorthands = new Dictionary<string, string>() {
            { "App", typeof(App).FullName }, 
            { "Log", typeof(App.Log).FullName },
            { "LogLevel", typeof(App.Log.Levels).FullName },
            { "Console", typeof(System.Console).FullName },
            { "Time", typeof(System.DateTime).FullName }
        };

        public jsObject<CustomType> Types = new jsObject<CustomType>();
        public jsObject<Helper.Initializer> RawInitializerCache = new jsObject<Helper.Initializer>();

        public Engine() {
            Using.Add(Libraries.Standard.Instance);
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
