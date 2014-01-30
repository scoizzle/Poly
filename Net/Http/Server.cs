using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Http {
    using Data;
    using Event;
    using Net.Tcp;
    using Script;

    public partial class Server : MultiPortServer {
        public jsObject<Host> Hosts = new jsObject<Host>();

        public FileCache FileCache = new FileCache();
        public ScriptCache ScriptCache = new ScriptCache();

        public string GetMime(string Ext) {
            var Mime = Poly.Mime.GetMime(Ext);

            if (!string.IsNullOrEmpty(Mime))
                return Mime;

            return "text/html";
        }

        public void Configure(string FileName) {
            var Args = new jsObject(
                "Server", this
            );

            Script.Engine Eng = new Script.Engine();

            Eng.Parse(
                File.ReadAllText(FileName)
            );

            Eng.Evaluate(Args);
        }

        public void On(string Key, Handler Handler) {
            RegisterRoute(Key, Handler);
        }

        public void Host(string Name, jsObject Info) {
            Hosts[Name] = new Host(Info) {
                Name = Name
            };
        }

        public void Mime(string Ext, string Mime) {
            Poly.Mime.List[Ext] = Mime;
        }
    }
}
