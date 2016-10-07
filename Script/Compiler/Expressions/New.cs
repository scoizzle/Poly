using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Script.Compiler.Expressions {
    using Nodes;
    using Parser;
    using Helpers;

    public class New : Value {
        new public static Node Parse(Context Context) {
            if (Context.Consume("new")) {
                Context.ConsumeWhitespace();

                var Type = Context.ParseType();

                if (Type != null) {
                    return new New { Type = Context.TypeInformation.GetInformation(Type) };
                }
            }
            return null;
        }
    }
}
