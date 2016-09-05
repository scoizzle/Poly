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
                : base('`' + fmt + '`') {
                Blocks = Matcher.Parse(fmt);
            }

            public override bool Match(Context Ctx) {
                if (Blocks == null) return false;

				var Storage = Ctx.Storage;
				
                var Context = new Context(Ctx.String) {
                    Index = Ctx.Index,
                    BlockCount = Blocks?.Length ?? 0
                };
                
                while (Matcher.Match(Blocks, Context)) {
                    if (Context.Storage.Count == 2) {
                        var Key = Context.Storage["Key"];
                        var Value = Context.Storage["Value"];

                        if (Key != null && Value != null)
                            Storage.Set(Key as string, Value);
                    }
                    else if (Context.Storage.Count == 1) {
                        var Value = Context.Storage["Value"];

                        if (Value != null)
                            Storage.Add(Value);
                    }

                    Context.Storage.Clear();
                    Context.BlockIndex = 0;

                    if (Context.IsDone())
                        break;
                }

                Ctx.Index = Context.Index;
                return true;
            }
        }
    }
}
