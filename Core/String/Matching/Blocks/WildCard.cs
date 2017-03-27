using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;
    
    public partial class Matcher {
        class WildCard : Block {
            public WildCard()
                : base("*") {
            }
            
            private MatchDelegate _Handler;

            internal override bool ValidCharacter(char Char) {
                return true;
            }

            internal override void Prepare() {
                if (Next == null) {
                    _Handler = __End;
                }
                else if (Next is Static) {
                    _Handler = __Static;
                }
                else {
                    _Handler = __Default;
                }
            }
            
            public override bool Match(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                return _Handler(Data, ref Index, ref BlockIndex, Store);
            }

            public override bool Template(StringBuilder Output, JSON Context) {
                return true;
            }

            private bool __End(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                Index = Data.Length;
                return true;
            }

            private bool __Default(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                while (Index < Data.Length) {
                    if (Next.ValidCharacter(Data[Index]))
                        return Next.Match(Data, ref Index, ref BlockIndex, Store);

                    Index++;
                }

                return true;
            }

            private bool __Static(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var S = Next.Format;
                var L = S.Length;
                var i = Data.Find(S, Index, Data.Length);

                if (i != -1) {
                    Index = i + L;
                    BlockIndex++;
                    return true;
                }
                return IsOptional;
            }
        }
    }
}
