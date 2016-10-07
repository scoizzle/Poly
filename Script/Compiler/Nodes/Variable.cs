using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;

namespace Poly.Script.Compiler.Nodes {
    using Parser;

    public class Variable : Value {
        public string Name;
        public Node Value;

        public Variable(string name) : this(name, null) { }
        public Variable(string name, Node value) {
            Name = name;
            Value = value;
        }

        new public static Variable Parse(Context Ctx) {
            int Start = Ctx.Index;
            string Name = string.Empty;

            if (Ctx.Current == '_' || char.IsLetter(Ctx.Current)) {
                Ctx.Consume(Context.NameFuncs);

                Name = Ctx.Substring(Start, Ctx.Index - Start);

                Variable Cached = Ctx.DefinedVariables[Name];

                if (Cached != null) return Cached;
                else Ctx.DefinedVariables.Add(Name, new Variable(Name));
            }
            return null;
        }
    }
}
