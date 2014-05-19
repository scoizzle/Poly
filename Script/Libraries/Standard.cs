using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class Standard : Library {
        public static Standard Instance;
        public Standard() {
            Instance = this;
            Library.RegisterLibrary("Standard", this);

            Add(Sleep);
            Add(Url);
            Add(Libraries.Global.Load);
        }

        public static SystemFunction Sleep = new SystemFunction("Sleep", (Args) => {
            int Delay = Args.Get<int>("0");

            System.Threading.Thread.Sleep(Delay);

            return null;
        });

        public static SystemFunction Url = new SystemFunction("Url", (Args) => {
            var Raw = Args.Get<string>("0");

            if (!string.IsNullOrEmpty(Raw))
                return new Poly.Net.Url(Raw);

            return null;
        });
    }
}
