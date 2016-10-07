using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Script.Compiler.Expressions {
    using Nodes;
    using Parser;
    using Helpers;

    public class Using : Value {
        new public static Node Parse(Context Context) {
            int split;
            string name, typeName;
            Type typeRef;

            if (Context.Consume("using")) {
                Context.ConsumeWhitespace();

                name = Context.ExtractUntil(';');
                if (name == null) return null;

                split = name.IndexOf('=');
                if (split == -1) Context.Namespaces.Add(name);
                else {
                    typeName = name.Substring(split + 1).Trim();
                    name = name.Substring(0, split).Trim();
                    typeRef = System.Type.GetType(typeName, false, true);

                    if (typeRef == null) return null;

                    Context.TypeShorthands.Add(name, typeRef);
                }

                return NoOperation;
            }
            return null;
        }
    }
}
