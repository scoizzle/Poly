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
        public Host[] Hosts = new Host[0];
        public jsObject<Session> Sessions = new jsObject<Session>();

        public Server() {
            base.OnClientConnect += this.OnClientConnect;
        }
        
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
            var Host = new Host();

            Info.CopyTo(Host);

            this.Host(Name, Host);

            return Host;
        }

        public Host Host(string Name, Host Host) {
            foreach (int? Port in Host.Ports.Values) {
                if (Port != null)
                    Listen((int)Port);
            }

            Host.Matcher = new Matcher(Name);

            Array.Resize(ref Hosts, Hosts.Length + 1);
            Hosts[Hosts.Length - 1] = Host;
            return Host;
        }

        public void Mime(string Ext, string Mime) {
            Poly.Mime.List[Ext] = Mime;
        }
    }
}
