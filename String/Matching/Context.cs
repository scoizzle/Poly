using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;

    public partial class Matcher {
        class Context : StringIterator {
            public bool Store;
            public jsObject Storage;
            public int BlockIndex, BlockCount;

            public Context(string Data) : base(Data) {
                this.Storage = new jsObject();
                this.Store = true;
            }

            public Context(string Data, jsObject Storage) : base(Data) {
                this.Storage = Storage;
                this.Store = true;
            }
        }
    }
}
