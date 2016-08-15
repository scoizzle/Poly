using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    public class DateTime : Library {
        public DateTime() {
            RegisterStaticObject("DateTime", this);

            Add(ToUnixTime);
            Add(FromUnixTime);
        }

        public static Function ToUnixTime = Function.Create<System.DateTime>("ToUnixTime", dt => (object)DateTimeExtensions.ToUnixTime(dt));
        public static Function FromUnixTime = Function.Create<double>("FromUnixTime", ut => DateTimeExtensions.FromUnixTime(ut));
    }    
}
