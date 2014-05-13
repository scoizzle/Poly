using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class Hash : Library {
        public Hash() {
            RegisterStaticObject("Hash", this);

            Add(MD5);
            Add(SHA1);
            Add(SHA256);
            Add(SHA512);
        }

        public static SystemFunction MD5 = new SystemFunction("MD5", (Args) => {
            var Obj = Args.Get<object>("0");

            if (Obj != null)
                return Obj.ToString().MD5();

            return null;
        });

        public static SystemFunction SHA1 = new SystemFunction("SHA1", (Args) => {
            var Obj = Args.Get<object>("0");

            if (Obj != null)
                return Obj.ToString().SHA1();

            return null;
        });

        public static SystemFunction SHA256 = new SystemFunction("SHA256", (Args) => {
            var Obj = Args.Get<object>("0");

            if (Obj != null)
                return Obj.ToString().SHA256();

            return null;
        });

        public static SystemFunction SHA512 = new SystemFunction("SHA512", (Args) => {
            var Obj = Args.Get<object>("0");

            if (Obj != null)
                return Obj.ToString().SHA512();

            return null;
        });
    }
}
