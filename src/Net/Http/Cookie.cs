using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    using Collections;

    public class Cookie {
        public string Name;
        public string Value;

        public string Domain;
        public string Path;

        public DateTime Expires;

        public bool HttpOnly;
        public bool Secure;
    }
}