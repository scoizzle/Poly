using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System {
    static class DateTimeExtensions {
        public static readonly DateTime UnixEpoc = new DateTime(1970, 1, 1);

        public static double ToUnixTime(this DateTime This) {
            return (This.ToUniversalTime() - UnixEpoc).TotalSeconds;
        }

        public static DateTime FromUnixTime(this double This) {
            return UnixEpoc.AddSeconds(This).ToLocalTime();
        }
    }
}
