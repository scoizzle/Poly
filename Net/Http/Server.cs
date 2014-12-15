using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Http {
    using Data;
    using Net.Tcp;
    using Script;

    public partial class Server : MultiPortServer {
        public jsObject<Host> Hosts = new jsObject<Host>();
        public jsObject<Session> Sessions = new jsObject<Session>();

        public FileCache FileCache = new FileCache();
        public ScriptCache ScriptCache = new ScriptCache();

        public static string GetMime(string Ext) {
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

        public Host Host(string Name, jsObject Info) {
            var Host = new Host(Info) {
                Name = Name
            };

            this.Host(Name, Host);

            return Host;
        }

        public Host Host(string Name, Host Host) {
            foreach (int Port in Host.Ports.Values) {
                if (!Listeners.ContainsKey(Port)) {
                    Listen(Port);
                }
            }

            Hosts[Name.Replace(".", "\\.")] = Host;
            return Host;
        }

        public void Mime(string Ext, string Mime) {
            Poly.Mime.List[Ext] = Mime;
        }
    }
}
