using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class TimeSpan : Library {
        public TimeSpan() {
            RegisterTypeLibrary(typeof(System.TimeSpan), this);

            Add(ToDurationString);
        }

        public static SystemFunction ToDurationString = new SystemFunction("ToDurationString", 
            Event.Wrapper<System.TimeSpan>(Poly.TimeSpanExtensions.ToDurationString, "this")
        );
    }
}
