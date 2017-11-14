using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class HttpExtensions {
        const string HttpTimeFormat = "ddd, dd MMM yyy hh:mm:ss";

        public static DateTime FromHttpTimeString(this string Str) {
            return DateTime.ParseExact(Str, HttpTimeFormat, Globalization.DateTimeFormatInfo.CurrentInfo);
        }

        public static string ToHttpTimeString(this DateTime Time) {
            return Time.ToString(HttpTimeFormat) + " GMT";
        }
    }
}
