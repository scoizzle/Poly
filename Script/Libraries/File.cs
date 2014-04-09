using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using IO = System.IO;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class File : Library {
        public File() {
            RegisterStaticObject("File", this);

            Add(Exists);
            Add(Delete);
            Add(ReadAllText);
            Add(WriteAllText);
        }

        public static SystemFunction Exists = new SystemFunction("Exists", (Args) => {
            return IO.File.Exists(Args.Get<string>("FileName"));
        }, "FileName");

        public static SystemFunction Delete = new SystemFunction("Delete", (Args) => {
            try {
                IO.File.Delete(Args.Get<string>("FileName"));
                return true;
            }
            catch { }
            return false;
        }, "FileName");

        public static SystemFunction ReadAllText = new SystemFunction("ReadAllText", (Args) => {
            var Name = Args.Get<string>("FileName");

            if (IO.File.Exists(Name)) {
                try {
                    return IO.File.ReadAllText(Name);
                }
                catch { }
            }

            return string.Empty; ;
        }, "FileName");

        public static SystemFunction WriteAllText = new SystemFunction("WriteAllText", (Args) => {
            var Name = Args.Get<string>("FileName");
            var Content = Args.Get<string>("Content");

            try {
                IO.File.WriteAllText(Name, Content);
                return true;
            }
            catch { }

            return false;
        }, "FileName", "Content");
    }
}
