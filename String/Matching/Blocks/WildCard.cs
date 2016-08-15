using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public partial class Matcher {
        class WildCard : Block {
            public WildCard()
                : base("*") {
            }

            public override bool Match(Context Context) {
                if (Next == null) {
                    Context.Index = Context.Length;
                    return true;
                }
                else {
                    if (Next is Extract) {
                        var Ext = Next as Extract;

                        while (!Context.IsDone()) {
                            if (Ext.ValidChar(Context.Current))
                                return Next.Match(Context);

                            Context.Tick();
                        }
                    }
                    else
                    if (Next is Static) {
						if ((Context.BlockCount - Context.BlockIndex) == 2) {
							if (Context.EndsWith (Next.Format)) {
								Context.Index = Context.Length;
                                Context.BlockIndex++;
								return true;
							}
						}
								
                        return Context.Goto(Next.Format);
                    }

                    return false;
                }
            }
        }
    }
}
