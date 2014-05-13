using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class Time : Library {
        public Time() {
            RegisterStaticObject("Time", this);

            Add(Now);
            Add(UtcNow);
            Add(Ticks);
            Add(Since);
        }

        public static SystemFunction Ticks = new SystemFunction("Ticks", (Args) => {
            return DateTime.Now.Ticks;
        });

        public static SystemFunction Since = new SystemFunction("Since", (Args) => {
            var ticks = Args.Get<long>("0");

            if (ticks == default(long))
                return -1;

            return (DateTime.Now - DateTime.FromBinary(ticks)).Ticks;
        });

        public static SystemFunction Now = new SystemFunction("Now", (Args) => {
            var Fmt = Args.Get<string>("0");

            if (string.IsNullOrEmpty(Fmt)) {
                return DateTime.Now.ToString();
            }

            return DateTime.Now.ToString(Fmt);
        });

        public static SystemFunction UtcNow = new SystemFunction("UtcNow", (Args) => {
            var Fmt = Args.Get<string>("0");

            if (string.IsNullOrEmpty(Fmt)) {
                return DateTime.UtcNow.ToString();
            }

            return DateTime.UtcNow.ToString(Fmt);
        });
    }
}
