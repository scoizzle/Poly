using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Script.Compiler.Parser {
    using Nodes;

    public partial class Context {
        public static List<Parser> ExpressionParsers = new List<Parser>() {
        };

        public static List<Parser> ValueParsers = new List<Parser>() {
        };

        public Node Parse() {
            return ParseExpression() ?? ParseValue();
        }

        public Node ParseExpression() {
            Consume(WhitespaceFuncs);

            if (!IsDone()) {
                Node Current = null;
                if (ExpressionParsers.Any(f => (Current = f(this)) != null)) {
                    return Current;
                }
            }

            return null;
        }

        public Node ParseValue() {
            Consume(WhitespaceFuncs);

            if (!IsDone()) {
                Node Current = null;
                if (ValueParsers.Any(f => (Current = f(this)) != null)) {
                    return Current;
                }
            }

            return null;
        }

        public Node ParseOperation() {
            var Node = ParseValue();

            //while (!IsDone()) {
            //    ConsumeWhitespace();

            //    var List = Node == null ?
            //        Expressions.Unary.Parser.RightHandedParsers :
            //        Expressions.Unary.Parser.Parsers;

            //    if (!List.Any(p => {
            //        if (Consume(p.Key)) {
            //            ConsumeWhitespace();

            //            Node = p.Value(Engine, Node);
            //            return true;
            //        }
            //        return false;
            //    })) break;
            //}

            return Node;
        }

        public Node ParseExpressions(Node Storage) {
            var List = new List<Node>();

            while (!IsDone()) {
                var Node = ParseExpression();

                if (Node == null)
                    break;

                if (Node == Node.NoOperation)
                    continue;

                List.Add(Node);
            }

            if (List.Count != 0)
                Storage.Elements = List.ToArray();

            return Storage;
        }

        public Node ParseValues(Node Storage) {
            var List = new List<Node>();

            while (!IsDone()) {
                var Node = ParseValue();

                if (Node == null)
                    break;

                List.Add(Node);
            }

            if (List.Count != 0)
                Storage.Elements = List.ToArray();

            return Storage;
        }

        public Node ParseOperations(Node Storage) {
            var List = new List<Node>();

            while (!IsDone()) {
                var Node = ParseOperation();

                if (Node == null)
                    break;

                List.Add(Node);
            }

            if (List.Count != 0)
                Storage.Elements = List.ToArray();

            return Storage;
        }

        public bool Parse(string Text) {
            return ParseExpressions(new Node()) != null;
        }
    }
}
