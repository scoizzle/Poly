using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;

    public partial class Matcher {
        class Block {
            public string Format;

            public Block Next;

            public Block(string fmt) { Format = fmt; }

            public virtual bool Match(Context Context) { return false; }

            public virtual bool Template(StringBuilder Output, jsObject context) { return false; }

            public override string ToString() { return Format; }
        }
    }
}
