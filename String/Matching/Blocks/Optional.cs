using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public partial class Matcher {
        class Optional : Block {
			internal Block[] Blocks;

            public Optional(string Fmt)
                : base(Fmt) {
				Blocks = Matcher.Parse(Fmt);
            }

            public override bool Match(Context Context) {
                var c = new Context(Context.String) { Index = Context.Index };

				if (Matcher.Match(Blocks, c)) {
                    Context.Index = c.Index;
                    Context.BlockIndex += c.BlockIndex - Blocks.Length;

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
