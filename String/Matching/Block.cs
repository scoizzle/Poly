using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;

    public partial class Matcher {
        class Block {
            public bool IsLast;
            public bool IsOptional;

            public string Format;
            public Block Next;

            public delegate bool MatchDelegate(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store);

            public Block(string fmt) { Format = fmt; }

            public virtual bool Match(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) { return false; }
            
            internal virtual bool ValidCharacter(char Char) { return false; }

            internal virtual void Prepare() { }

            public virtual bool Template(StringBuilder Output, JSON Context) { return false; }

            public override string ToString() { return Format; }
        }
    }
}
