using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    public class Standard : Library {
        public Standard() {
            Library.Standard = this;

            Add(Sleep);
            Add(Url);
            Add(Libraries.Global.Load);
        }

        public static Function Sleep = new Function("Sleep", (Args) => {
            int Delay = Args.Get<int>("0");

            System.Threading.Thread.Sleep(Delay);

            return null;
        });

        public static Function Url = new Function("Url", (Args) => {
            var Raw = Args.Get<string>("0");

            if (!string.IsNullOrEmpty(Raw))
                return new Poly.Net.Url(Raw);

            return null;
        });
    }
}
