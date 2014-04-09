using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    public static class Http {
        public static string HttpTimeString(this DateTime Time) {
            return Time.ToUniversalTime().ToString("ddd, dd MMM yyy hh:mm:ss") + " GMT";
        }
    }
}
