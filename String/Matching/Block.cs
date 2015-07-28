using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public partial class Matcher {
        class Block {
            public string Format;

            public Block Next;

            public Block(string fmt) {
                this.Format = fmt;
            }

            public virtual bool Match(Context Context) { return false; }

            public override string ToString() {
                return Format;
            }
        }
    }
}
