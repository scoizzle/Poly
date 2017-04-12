using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class HttpExtensions {
        public static string HttpTimeString(this DateTime Time) {
            return Time.ToString("ddd, dd MMM yyy hh:mm:ss") + " GMT";
        }
    }
}
