using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public partial class Matcher {
        class Static : Block {
            public Static(string Str)
                : base(Str) {
            }

            public override bool Match(Context Context) {
                return Context.Consume(Format);
            }
        }
    }
}
