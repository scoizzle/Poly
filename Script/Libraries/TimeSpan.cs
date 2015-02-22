using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    public class TimeSpan : Library {
        public TimeSpan() {
            RegisterTypeLibrary(typeof(System.TimeSpan), this);

            Add(ToDurationString);
        }


        public static Function ToDurationString = Function.Create<System.TimeSpan>("ToDurationString", Poly.TimeSpanExtensions.ToDurationString);
    }
}
