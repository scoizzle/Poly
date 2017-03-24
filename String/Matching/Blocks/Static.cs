using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;

    public partial class Matcher {
        class Static : Block {
            public Static(string Str)
                : base(Str) {
                First = Str.FirstOrDefault();
            }

            char First;
            MatchDelegate Handler;

            internal override void Prepare() {
                if (Format.Length == 1)
                    Handler = __Char;
                else
                    Handler = __String;
            }

            internal override bool ValidCharacter(char Char) {
                return Char == First;
            }

            public override bool Match(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                return Handler(Data, ref Index, ref BlockIndex, Store);
            }

            public override bool Template(StringBuilder Output, JSON Context){
                Output.Append(Format);
                return true;
            }

            private bool __String(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var S = Format;
                var L = S.Length;

                if (string.Compare(Data, Index, S, 0, S.Length, StringComparison.Ordinal) == 0) {
                    Index += L;
                    return true;
                }

                return false;
            }

            private bool __Char(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                if (Index < Data.Length)
                if (Data[Index] == First) {
                    Index++;
                    return true;
                }

                return false;
            }
        }
    }
}
