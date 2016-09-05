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

			public Context(string Data) : this(Data, new jsObject()) { }

            public Context(string Data, jsObject storage) : base(Data) {
                Storage = storage;
                Store = true;
            }

			public void Set(string Key, object Value) {
				if (!Store) return;
				Storage.AssignValue(Key, Value);
			}
        }
    }
}
