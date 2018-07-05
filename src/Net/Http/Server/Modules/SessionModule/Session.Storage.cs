using System;
using System.Collections.Generic;

namespace Poly.Net.Http {
    public partial class SessionModule {
        public class Session : Dictionary<object, object> {
            public Guid Id;

            public DateTime LastAccessTime;

            public Session(Guid id) : base() {
                Id = id;
                LastAccessTime = DateTime.Now;
            }
        }
    }
}
