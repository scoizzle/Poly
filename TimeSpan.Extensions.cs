using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public static class TimeSpanExtensions {
        public static string ToDurationString(this TimeSpan This) {
            StringBuilder Output = new StringBuilder();

            int Years = This.Days / 365;
            int Days = Years > 0 ? 
                This.Days % 365 :
                This.Days;
            
            if (Years > 0) {
                Output.AppendFormat("{0}y ", Years);
            }

            if (Days > 0) {
                Output.AppendFormat("{0}d ", Days);
            }

            if (This.Hours > 0) {
                Output.AppendFormat("{0}h ", This.Hours);
            }

            Output.AppendFormat("{0}m ", This.Minutes);
            Output.AppendFormat("{0}s", This.Seconds);

            return Output.ToString();
        }
    }
}
