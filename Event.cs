using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly {
    public partial class Event {
        public delegate object Handler(jsObject Args);

        public static object Invoke(Handler Func, jsObject Args) {
            if (Func == null)
                return null;

            return Func(Args);
        }

        public static object Invoke(Handler Func, jsObject Args, params object[] ArgPairs) {
            if (Func == null)
                return null;

            for (int i = 0; i < ArgPairs.Length / 2; i++) {
                Args[ArgPairs[i].ToString()] = ArgPairs[i + 1];
                i++;
            }

            return Func(Args);
        }
    }
}
