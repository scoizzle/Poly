using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;

    public partial class SessionModule {
        public class Session : Dictionary<object, object> {
            public Guid Id;

            public Session(Guid id) : base() {
                Id = id;
            }
        }
    }
}
