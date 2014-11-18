using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    class Comment : Expression {
        public static new Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("//", Index) || Text.Compare('#', Index)) {
                var Delta = Index + 2;

                while (Delta < LastIndex) {
                    if (Text.Compare('\n', Delta))
                        break;

                    Delta++;
                }

                Index = Delta;
                return Expression.NoOperation;
            }
            else if (Text.Compare("/*", Index)) {
                var Delta = Index + 2;

                while (Delta < LastIndex) {
                    if (Text.Compare("*/", Delta))
                        break;

                    Delta++;
                }

                Index = Delta;
                return Expression.NoOperation;
            }

            return null;
        }
    }
}
