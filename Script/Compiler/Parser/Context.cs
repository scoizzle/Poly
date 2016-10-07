using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Script.Compiler.Parser {
    using Data;
    using Nodes;

    public delegate Node Parser(Context Context);

    public partial class Context : StringIterator {
        public Script Script;
        public List<string> Namespaces;
        public KeyValueCollection<Type> TypeShorthands;
        public KeyValueCollection<Variable> DefinedVariables;

        public Context(Script script, string Text) : base(Text) {
            Script = script;

            Namespaces = new List<string>() {
                "System",
                "Poly"
            };

            TypeShorthands = new KeyValueCollection<Type>() {
                { "bool", typeof(bool) },
                { "byte", typeof(byte) },
                { "short", typeof(short) },
                { "int", typeof(int) },
                { "long", typeof(long) },
                { "float", typeof(float) },
                { "double", typeof(double) },
                { "decimal", typeof(decimal) },
                { "object", typeof(object) },
                { "string", typeof(string) }
            };
        }
    }
}
