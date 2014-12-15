using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;
using Poly.Script.Nodes;

namespace Poly.Script {
    using Nodes;
    using Types;
    using Expressions;
    using Helpers;

    public partial class Engine {
        public static List<Parser> ExpressionParsers = new List<Parser>() {
            Comment.Parse,
            If.Parse,
            For.Parse,
            Foreach.Parse,
            Do.Parse,
            While.Parse,
            Switch.Parse,
            Case.Parse,
            Async.Parse,
            Try.Parse,
            Expressions.Using.Parse,
            Include.Parse,
            Reload.Parse,
            Return.Parse,
            Function.Parse,
            Object.Parse,
            Class.Parse,
            Expression.Parse,
            Call.Parse,
            Expressions.Html.Parser.Parse,
            Expressions.Unary.Parser.Parse,
            Expressions.Eval.Parse,
            String.Parse,
            Integer.Parse,
            Float.Parse,
            Bool.Parse,
            Variable.Parse
        };

        public Node Parse(string Text, int Index, Node Storage = null) {
            return Parse(Text, ref Index, Text.Length > 1 ? Text.Length : 1, Storage);
        }

        public Node Parse(string Text, ref int Index, int LastIndex, Node Storage = null) {
            if (!IsParseOk(this, Text, ref Index, LastIndex))
                return null;

            if (string.IsNullOrEmpty(Text)) {
                return Expression.NoOperation;
            }

            var List = Storage != null && Storage.Elements != null ?
                new List<Node>(Storage.Elements) :
                new List<Node>();

            while (Index < LastIndex) {
                Node Node = null;

                ConsumeWhitespace(Text, ref Index);

                if (Index >= LastIndex)
                    break;

                for (int i = 0; i < ExpressionParsers.Count; i++) {
                    Node = ExpressionParsers[i](this, Text, ref Index, LastIndex);

                    if (Node == null)
                        continue;

                    if (Storage == null) {
                        return Node;
                    }
                    else if (Node == Expression.NoOperation) {
                        break;
                    }
                    else {
                        List.Add(Node);
                        break;
                    }
                }

                if (Node == null) {
                    return null;
                }
                else if (Node == Expression.Continue || Node == Expression.Break) {
                    break;
                }
            }

            if (List.Count != 0) {
                Storage.Elements = List.ToArray();
            }
            return Storage;
        }

        public bool Parse(string Text) {
            int Index = 0;

            if (string.IsNullOrEmpty(Text)) {
                return false;
            }

            return Parse(Text, ref Index, Text.Length, this) != null;
        }
    }
}
