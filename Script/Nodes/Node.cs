using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class Node : jsObject<Node> {
        public Poly.Event.Handler GetSystemHandler() {
            return Evaluate;
        }

        public virtual object Evaluate(jsObject Context) {
			foreach (var Obj in this.Values) {
                if (Obj is Return)
                    return Obj;

                var Result = GetValue(Obj, Context);

                if (Result == Expression.Break || Result == Expression.Continue)
                    return Result;

                if (Obj is Operator)
                    continue;

                if (Result != null && Obj is Expression && !(Obj is Call)) {
                    return Result;
                }
			}

            return null;
        }

        public override string ToString() {
            return "";
        }

        public override string ToString(bool HumanFormat) {
            return this.ToString();
        }

        public static object GetValue(object Obj, jsObject Context) {
            if (Context == null || Obj == null)
                return null;

            var Node = Obj as Node;
            if (Node != null) {
                if (Node is Function) {
                    return Function.GetFunctionHandler(Node as Function, Context);
                }
                return Node.Evaluate(Context);
            }

            return Obj;
        }

        public static bool IsValidChar(char c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || (c == '_') || (c == '.');
        }

        public static void ConsumeBetween(string Text, ref int Index, string Open, string Close) {
            int X, Y, Z = 1;

            X = Text.IndexOf(Open, Index);

            for (Y = X + Open.Length; Y < Text.Length; Y++) {
                if (Text[Y] == '\\') {
                    Y++;
                    continue;
                }

                if (Text.Compare(Open, Y)) {
                    if (Open == Close) {
                        break;
                    }
                    else {
                        Z++;
                        continue;
                    }
                }

                if (Text.Compare(Close, Y)) {
                    Z--;

                    if (Z == 0) {
                        break;
                    }
                }
            }

            Index = Y + Close.Length;
        }

        public static void ConsumeExpression(string Text, ref int Index) {
            ConsumeBetween(Text, ref Index, "{", "}");
        }

        public static void ConsumeEval(string Text, ref int Index) {
            ConsumeBetween(Text, ref Index, "(", ")");
        }

        public static void ConsumeBlock(string Text, ref int Index) {
            ConsumeBetween(Text, ref Index, "[", "]");
        }

        public static void ConsumeString(string Text, ref int Index) {
            if (Text[Index] == '"')
                ConsumeBetween(Text, ref Index, "\"", "\"");
            else
                ConsumeBetween(Text, ref Index, "'", "'");
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

        public static void ConsumeWhitespace(string Text, ref int Index) {
            while (Index < Text.Length && (char.IsWhiteSpace(Text[Index]) || Text[Index] == ';'))
                Index++;
        }

        public static void ConsumeValidName(string Text, ref int Index) {
            var Delta = Index;

            for (; Delta < Text.Length;) {
                if (IsValidChar(Text[Delta]))
                    Delta++;
                else if (Text[Delta] == '[')
                    ConsumeBlock(Text, ref Delta);
                else break;
            }

            Index = Delta;
        }

        public static string ExtractValidName(string Text, ref int Index) {
            var Open = Index;

            ConsumeValidName(Text, ref Index);

            return Text.Substring(Open, Index - Open);
        }

        public static bool IsValidName(string Text, int Index) {
            var Delta = Index;
            
            ConsumeValidName(Text, ref Delta);

            return Index != Delta;
        }
        
        public static bool IsParseOk(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (Engine == null || string.IsNullOrEmpty(Text) || Index >= Text.Length || LastIndex > Text.Length)
                return false;

            ConsumeWhitespace(Text, ref Index);
            return true;
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int Length) {
            return null;
        }

        public static jsObject AsJsObject(object Obj) {
            return Obj as jsObject;
        }
    }
}
