using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
	using Nodes;
	using Types;

	public class Persist : Expression {
        new public static Node Parse(Engine Engine, StringIterator It) {
            if (It.Consume("persist")) {
                It.ConsumeWhitespace();

                var Name = String.Parse(Engine, It) as StaticValue;

                if (Name != null) {
                    It.ConsumeWhitespace();

                    if (It.Consume("->")) {
                        It.ConsumeWhitespace();

                        var Var = Variable.Parse(Engine, It) as Variable;

                        if (Var != null && (Var.IsGlobal || Var.IsStatic)) {
                            var FileName = Engine.IncludePath + Name.Value as string;

                            Engine.PersistentFiles.Set(FileName,
                                new Helpers.PersistentFile(FileName, Var)
                            );

                            return Expression.NoOperation;
                        }
                    }
                }
            }

            return null;
        }
	}
}
