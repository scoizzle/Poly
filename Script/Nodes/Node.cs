using System;
using System.Text;

using Poly.Data;

namespace Poly.Script.Nodes {
    public class Node : IDisposable {
        public Node[] Elements;

        public void Dispose() { Elements = null; }

        public virtual object Evaluate(jsObject Context) {
            if (Elements == null)
                return null;

            foreach (Node N in Elements) {
                var R = N as Expressions.Return;
                if (R != null)
                    return R;

                var Value = N.Evaluate(Context);

                if (Value == null)
                    continue;

                if ((R = Value as Expressions.Return) != null)
                    return R;

                if (Value == Expression.Break || Value == Expression.Continue)
                    return Value;
            }
            return null;
        }

        public override string ToString() {
            if (Elements == null) 
                return string.Empty;

            StringBuilder Output = new StringBuilder();

            foreach (var Elem in Elements) {
                Output.Append(Elem.ToString()).AppendLine(";");
            }

            return Output.ToString();
        }

        public static bool IsValidChar(char c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || (c == '_') || (c == '.');
        }

        public static void ConsumeWhitespace(string Text, ref int Index) {
            while (Index < Text.Length) {
                StringIteration.ConsumeWhitespace(Text, ref Index);

                if (Index > -1 && Index < Text.Length && (Text[Index] == ';' || Text[Index] == ','))
                    Index++;
                else break;
            }
        }

        public static void ConsumeExpression(string Text, ref int Index) {
            Text.ConsumeBetween(ref Index, "{", "}");
        }

        public static void ConsumeEval(string Text, ref int Index) {
            Text.ConsumeBetween(ref Index, "(", ")");
        }

        public static void ConsumeBlock(string Text, ref int Index) {
            Text.ConsumeBetween(ref Index, "[", "]");
        }

        public static bool ConsumeString(string Text, ref int Index) {
            if (Text.Compare('"', Index))
                Text.ConsumeBetween(ref Index, "\"", "\"");
            else if (Text.Compare('\'', Index))
                Text.ConsumeBetween(ref Index, "'", "'");
            else return false;
            return true;
        }

        public static void ConsumeContent(string Text, ref int Index) {
            for (; Index < Text.Length; ) {
                var C = Text[Index];

                if (IsValidChar(C)) {
                    Index++;
                    continue;
                }

                switch (C) {
                    default:
                        return;

                    case '(':
                        ConsumeEval(Text, ref Index);
                        break;

                    case '[':
                        ConsumeBlock(Text, ref Index);
                        break;

                    case '"':
                    case '\'':
                        ConsumeString(Text, ref Index);
                        break;
                }
            }
        }

        public static void ConsumeValidName(string Text, ref int Index) {
            var Delta = Index;

            for (; Delta < Text.Length; ) {
                if (IsValidChar(Text[Delta]) || Text[Delta] == '@')
                    Delta++;
                else if (Text[Delta] == '[')
                    ConsumeBlock(Text, ref Delta);
                else break;
            }

            Index = Delta;
        }

        public static string ExtractValidName(string Text, ref int Index) {
            var End = Index;
            ConsumeValidName(Text, ref End);

            if (End > Index)
                return Text.Substring(Index, End - Index);

            return string.Empty;
        }

        public static bool Cast<T>(object I, out T V) {
            if (I is T) {
                V = (T)(I);
                return true;
            }

            V = default(T);
            return false;
        }

        public static bool Cast<T1, T2>(object Left, out T1 L, object Right, out T2 R) {
            if (!Cast<T1>(Left, out L) || !Cast<T2>(Right, out R)) {
                L = default(T1);
                R = default(T2);

                return false;
            }

            return true;
        }

        public static bool IsParseOk(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (Engine == null || string.IsNullOrEmpty(Text) || LastIndex > Text.Length || Index >= LastIndex)
                return false;

            ConsumeWhitespace(Text, ref Index);
            return true;
        }
    }
}
