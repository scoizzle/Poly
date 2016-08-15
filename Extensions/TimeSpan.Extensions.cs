using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public static class TimeSpanExtensions {
        public static string ToDurationString(this TimeSpan This) {
            if (This.TotalDays > 365)
                if (This.TotalDays < 365 * 2)
                    return "1 year and {0} months".ToString((int)((This.TotalDays - 365) / 30.436875));
                else
                    return "{0} years".ToString((int)(This.TotalDays / 365));
            else 
            if (This.TotalDays > 30.436875)
                return "{0} months".ToString((int)(This.TotalDays / 30.436875));
            else
            if (This.TotalDays > 7)
                return "{0} weeks".ToString((int)(This.TotalDays / 7));
            else
            if (This.TotalDays == 1)
                return "yesterday";
            if (This.TotalDays > 1)
                return "{0} days".ToString(This.Days);
            else
            if (This.TotalHours > 1)
                return "{0} hours".ToString(This.Hours);
			else
			if (This.TotalMinutes == 1)
				return "1 minute";
			else
            if (This.TotalMinutes > 1)
                return "{0} minutes".ToString(This.Minutes);
            else
            return "{0} seconds".ToString(This.Seconds);
        }
    }
}
