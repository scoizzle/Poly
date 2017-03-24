using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;
    
    public partial class Matcher {
        
        class Whitespace : Block {
            public Whitespace()
                : base("^") {
            }

            internal override bool ValidCharacter(char Char) {
                return char.IsWhiteSpace(Char);
            }

            public override bool Match(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                if (char.IsWhiteSpace(Data[Index])) {
                    do {
                        Index++;
                    }
                    while (Index < Data.Length && Char.IsWhiteSpace(Data[Index]));
                    return true;
                }
                return IsOptional;
            }
            public override bool Template(StringBuilder Output, JSON Context){
                Output.Append(' ');
                return true;
            }
        }
    }
}
