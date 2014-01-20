using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class Console : Library {
        public Console() {
            RegisterStaticObject("Console", this);

            Add(Read);
            Add(ReadLine);
            Add(Write);
            Add(WriteLine);
        }

        public static SystemFunction Read = new SystemFunction("Read", (Args) => {
            return char.ConvertFromUtf32(System.Console.Read());
        });

        public static SystemFunction ReadLine = new SystemFunction("ReadLine", (Args) => {
            return System.Console.ReadLine();
        });

        public static SystemFunction Write = new SystemFunction("Write", (Args) => {
            var Obj = Args.Get<object>("Obj");

            if (Obj != null) {
                System.Console.Write(Obj.ToString());
            }

            return null;
        }, "Obj");

        public static SystemFunction WriteLine = new SystemFunction("WriteLine", (Args) => {
            var Obj = Args.Get<object>("Obj");

            if (Obj != null) {
                System.Console.WriteLine(Obj.ToString());
            }

            return null;
        }, "Obj");
    }
}
