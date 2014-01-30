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
        public jsObject<SystemFunction.Raw> RawFunctionCache = new jsObject<SystemFunction.Raw>();
        public jsObject<SystemFunction.Initializer> RawInitializerCache = new jsObject<SystemFunction.Initializer>();

        public Engine() {
            Using.Add(Library.Defined["Standard"]);
        }

        public object Evaluate(string Script, params object[] Args) {
            var Context = new jsObject(Args);

            if (Parse(Script))
                return Evaluate(Context);

            return null;
        }
    }
}
