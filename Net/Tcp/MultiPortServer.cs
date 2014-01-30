using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Tcp {
    public class MultiPortServer {
        public Dictionary<int, Tcp.Server> Listeners = new Dictionary<int, Tcp.Server>();

        public virtual void OnClientConnect(Client Client) { }

        public void Listen(int Port) {
            if (!Listeners.ContainsKey(Port))
                Listeners.Add(Port, new Tcp.Server(Port));
        }

        public void ListenSsl(int Port, string CertificateFile) {
            if (!Listeners.ContainsKey(Port))
                Listeners.Add(Port, new Tcp.Server(IPAddress.Any, Port, CertificateFile));
        }

        public void Start() {
            foreach (var Listener in Listeners.Values) {
                if (!Listener.Active) {
                    Listener.ClientConnect += OnClientConnect;
                    Listener.Start();
                }
            }
        }

        public void Stop() {
            foreach (var Listen in Listeners.Values) {
                Listen.Stop();
            }
        }
    }
}
