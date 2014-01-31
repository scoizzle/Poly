using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;
using Poly.Script.Node;

namespace Poly.Script {
    public partial class Engine : Expression {
        public static List<Helper.Parser> ExpressionParsers = new List<Helper.Parser>() {
            Comment.Parse,
            If.Parse,
            For.Parse,
            Foreach.Parse,
            Do.Parse,
            While.Parse,
            Switch.Parse,
            Case.Parse,
            Async.Parse,
            Node.Using.Parse,
            Node.Include.Parse,
            Return.Parse,
            Expression.Parse,
            Function.Parse,
            Call.Parse,
            Node.Unary.Parser.Parse,
            Node.Object.Parse,
            Node.String.Parse,
            Integer.Parse,
            Float.Parse,
            Bool.Parse,
            Variable.Parse
        };

        public object Parse(string Text, int Index, Node.Node Storage = null) {
            return Parse(Text, ref Index, Text.Length > 1 ? Text.Length : 1, Storage);
        }

        public object Parse(string Text, ref int Index, int LastIndex, Node.Node Storage = null) {
            if (!IsParseOk(this, Text, ref Index, LastIndex))
                return null;

            if (string.IsNullOrEmpty(Text)) {
                return NoOp;
            }

            while (Index < LastIndex) {
                object Node = null;

                if (Text[Index] == ';') {
                    Index++;
                }

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
                    else if (Node == NoOp) {
                        break;
                    }
                    else {
                        Storage.Add(Node);
                        break;
                    }
                }

                if (Node == null) {
                    return null;
                }
                else if (Node == Continue || Node == Break) {
                    break;
                }
            }

            return Storage;
        }

        public new bool Parse(string Text) {
            int Index = 0;

            if (string.IsNullOrEmpty(Text)) {
                return false;
            }

            return Parse(Text, ref Index, Text.Length, this) != null;
        }
    }
}
