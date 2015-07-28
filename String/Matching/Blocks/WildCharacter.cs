using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public partial class Matcher {
        class WildChar : Block {
            public WildChar()
                : base("?") {
            }

            public override bool Match(Context Context) {
                Context.Tick();
                return true;
            }
        }
    }
}
