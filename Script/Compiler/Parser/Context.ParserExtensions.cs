using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Script.Compiler.Parser {
    public partial class Context {
        public static Func<char, bool>[] 
            WhitespaceFuncs = new Func<char, bool>[] {
                char.IsWhiteSpace,
                c => c == ';' || c == ','
            },
            NameFuncs = new Func<char, bool>[] {
                char.IsLetterOrDigit,
                c => c == '.' || c == '_'
            },
            CallNameFuncs = new Func<char, bool>[] {
                char.IsLetterOrDigit,
                c => c == '.' || c == '_' || c == '[' || c == ']'
            };



        public bool ConsumeTypeName() {
            int Start = Index, End = Index;

            if (Current == '_' || char.IsLetter(Current)) {
                if (Consume(C => C == '_' || C == '.', char.IsLetterOrDigit)) {
                    End = Index;

                    if (char.IsWhiteSpace(Current)) {
                        ConsumeWhitespace();
                    }

                    if (Current == '<') {
                        Tick();

                        if (Goto('<', '>')) {
                            Tick();
                            return true;
                        }
                    }

                    Index = End;
                    return true;
                }
            }

            return false;
        }

        public string ExtractTypeName() {
            var Start = Index;

            if (ConsumeTypeName()) {
                return Substring(Start, Index - Start);
            }

            return null;
        }

        public Type ParseType() {
            var Name = ExtractTypeName();

            if (string.IsNullOrEmpty(Name)) return null;
            else return GetType(Name);
        }
    }
}
