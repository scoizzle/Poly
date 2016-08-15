using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions.Unary {
    using Nodes;

    public class Parser {
        public delegate Node _Handler(Engine Engine, StringIterator It, Node Left);

        public static Dictionary<string, _Handler> Parsers,
                                                   RightHandedParsers;

        static Parser() {
            Parsers = new Dictionary<string, _Handler>() {
                { ":", KeyValuePair.Parse },
                { "??", Comparative.Parse },
                { "?", Conditional.Parse },
                { "as", As.Parse },
                { "is", Is.Parse },
                { "->", Between.Parse },
                { "~", Match.Parse },
                { "~!", MatchAll.Parse },
                { "&&", And.Parse },
                { "||", Or.Parse },
                { "|", Template.Parse },
                { "!=", NotEqual.Parse },
                { "%", Modulus.Parse },
                { "==", Equal.Parse },
                { "=", Assign.Parse },
                { "<=", LessThanEqual.Parse },
                { "<", LessThan.Parse },
                { ">=", GreaterThanEqual.Parse },
                { ">", GreaterThan.Parse },
                { "+=", Add.Assignment },
                { "++", Add.Iterator },
                { "+", Add.Parse },
                { "-=", Subtract.Assignment },
                { "--", Subtract.Iterator },
                { "-", Subtract.Parse },
                { "*=", Multiply.Assignment },
                { "*", Multiply.Parse },
                { "/=", Devide.Assignment },
                { "/", Devide.Parse }
            };

            RightHandedParsers = new Dictionary<string, _Handler>() {
                { "!", NotNull.Parse },
                { ":", KeyValuePair.Parse },
                { "|", Template.Parse },
            };
        }
    }
}
