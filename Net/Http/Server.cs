using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Http {
    using Data;
    using Net.Tcp;
    using Script;

    public partial class Server : MultiPortServer {
        public ManagedArray<Host> Hosts;
        public int ClientReceiveTimeout { get; set; }

        public Server() {
            Hosts = new ManagedArray<Host>();

            OnClientConnect += ClientConnected;
        }

        public override void Start() {
            for (int i = 0; i < Hosts.Count; i++)
                Hosts.Elements[i].Ready();

            base.Start();
        }

        public override void Stop() {
            for (int i = 0; i < Hosts.Count; i++)
                Hosts.Elements[i].Stop();

            base.Stop();
        }

        public Host AddHost(string Name) {
            return AddHost(new Host(Name));
        }

        public Host AddHost(string Name, jsObject Info) {
            var Host = AddHost(Name);
            Info?.CopyTo(Host);
            return Host;
        }

        public Host AddHost(Host Info) {
            Hosts.Add(Info);
            return Info;
        }

        public void RemoveHost(string Name) {
            var Elems = Hosts.Elements;
            var Len = Hosts.Count;

            for (var i = 0; i < Len; i++) {
                var H = Elems[i];
                if (string.Compare(H.Name, Name, StringComparison.Ordinal) != 0) continue;

                H.Stop();
                Hosts.RemoveAt(i);
                break;
            }
        }

        public void Mime(string Ext, string Mime) {
            Poly.Mime.List[Ext] = Mime;
        }
    }
}
