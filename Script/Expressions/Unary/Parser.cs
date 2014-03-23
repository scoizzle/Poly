using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node.Unary {
    public class Parser {
        public delegate Operator Handler(Engine Engine, string Text, ref int Index, int LastIndex, string Left);
        public static List<Handler> UnaryParsers = new List<Handler>() {
            Equal.Parse,
            NotEqual.Parse,
            NotNull.Parse,
            LessThanEqual.Parse,
            LessThan.Parse,
            GreaterThanEqual.Parse,
            GreaterThan.Parse,
            And.Parse,
            Or.Parse,
            Between.Parse,
            Assign.Parse,
            Match.Parse,
            Multiply.Parse,
            Devide.Parse,
            Add.Parse,
            Subtract.Parse,
        };

        public static object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!Expression.IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            Expression.ConsumeContent(Text, ref Delta);

            if (Delta > LastIndex)
                return null;

            var Left = Text.Substring(Index, Delta - Index);
            Expression.ConsumeWhitespace(Text, ref Delta);

            for (int i = 0; i < UnaryParsers.Count; i++) {
                var U = UnaryParsers[i](Engine, Text, ref Delta, LastIndex, Left);

                if (U != null) {
                    Index = Delta;
                    return U;
                }
            }

            return null;
        }
    }
}
