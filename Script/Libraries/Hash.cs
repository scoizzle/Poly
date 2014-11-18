using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    public class Hash : Library {
        public Hash() {
            RegisterStaticObject("Hash", this);

            Add(MD5);
            Add(SHA1);
            Add(SHA256);
            Add(SHA512);
        }

        public static Function MD5 = new Function("MD5", (Args) => {
            var Obj = Args.Get<object>("0");

            if (Obj != null)
                return Obj.ToString().MD5();

            return null;
        });

        public static Function SHA1 = new Function("SHA1", (Args) => {
            var Obj = Args.Get<object>("0");

            if (Obj != null)
                return Obj.ToString().SHA1();

            return null;
        });

        public static Function SHA256 = new Function("SHA256", (Args) => {
            var Obj = Args.Get<object>("0");

            if (Obj != null)
                return Obj.ToString().SHA256();

            return null;
        });

        public static Function SHA512 = new Function("SHA512", (Args) => {
            var Obj = Args.Get<object>("0");

            if (Obj != null)
                return Obj.ToString().SHA512();

            return null;
        });
    }
}
