using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public partial class Matcher {
        class Optional : Block {
            Matcher Sub;

            public Optional(string Fmt)
                : base(Fmt) {
                    Sub = new Matcher(Fmt);
            }

            public override bool Match(Context Context) {
                var c = new Context(Context.String) { Index = Context.Index };

                if (Sub.Match(c)) {
                    Context.Index = c.Index;

                    if (c.Store)
                        c.Storage.CopyTo(Context.Storage);

                    return true;
                }

                return false;
            }

            public override string ToString() {
                return string.Format("[{0}]", Format);
            }
        }
    }
}
