using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    public class Http : Library {
        public Http() {
            RegisterStaticObject("Http", this);
            
            Add(Escape);
            Add(Descape);
            Add(Server);
        }

        public static Function Server = Function.Create("Server", (string hostName, int Port) => {
            return new Net.Http.Server(hostName, Port);
        });

        public static Function Escape = Function.Create("Escape", (string Str) => {
            return System.Uri.EscapeUriString(Str);
        });

        public static Function Descape = Function.Create("Descape", (string Str) => {
            return System.Uri.UnescapeDataString(Str);
        });
    }
}
