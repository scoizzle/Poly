using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;

    public partial class Matcher {
        class ExtractAll : Block {
            Block[] Blocks;

            public ExtractAll(string fmt)
                : base("`" + fmt + '`') {
                Blocks = Parse(fmt);
            }

            public override bool Match(Context Context) {
                if (Blocks == null) return false;

                var Extracts = Context.Extractions;

                if (Extracts.Count > 0)
                    Context.Extractions = new ManagedArray<Context.Extraction>();

                while (Matcher.Match(Blocks, Context)) {
                    Extracts.Add(new Context.GroupedExtraction(Context.Extractions));
                    Context.Extractions.Clear();
                    Context.BlockIndex = 0;
                }

                if (Extracts.Count > 0) {
                    Context.Extractions.CopyTo(Extracts);
                    Context.Extractions = Extracts;
                }

                return true;
            }
        }
    }
}
