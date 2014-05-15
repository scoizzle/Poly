using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Libraries {
    public class Logging : Library {
        public Logging() {
            RegisterStaticObject("Log", this);

            Add(Info);
            Add(Warning);
            Add(Error);
            Add(Fatal);
        }

        public static SystemFunction Info = new SystemFunction("Info", (Args) => {
            App.Log.Info(string.Join("", Args.Values));
            return null;
        });

        public static SystemFunction Warning = new SystemFunction("Warning", (Args) => {
            App.Log.Warning(string.Join("", Args.Values));
            return null;
        });

        public static SystemFunction Error = new SystemFunction("Error", (Args) => {
            App.Log.Error(string.Join("", Args.Values));
            return null;
        });

        public static SystemFunction Fatal = new SystemFunction("Fatal", (Args) => {
            App.Log.Fatal(string.Join("", Args.Values));
            return null;
        });
    }
}
