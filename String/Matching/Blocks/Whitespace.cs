using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public partial class Matcher {
        class Whitespace : Block {
            public Whitespace()
                : base("^") {
            }

            public override bool Match(Context Context) {
                if (Char.IsWhiteSpace(Context.Current)) {
                    Context.ConsumeWhitespace();
                    return true;
                }

                return false;
            }
        }
    }
}
