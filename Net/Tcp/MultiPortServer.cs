using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Tcp {
    public class MultiPortServer {
        public Dictionary<int, Server> Listeners = new Dictionary<int, Server>();

        public event Server.OnClientConnectDelegate OnClientConnect;

        public bool Active { get; private set; }

        public IPEndPoint Accept(int Port) {
            Server Serv;

            if (Listeners.ContainsKey(Port)) {
                Serv = Listeners[Port];
                return Serv.LocalEndpoint as IPEndPoint;
            }

            Serv = new Server(Port);
            Listeners.Add(Port, Serv);

            return Serv.LocalEndpoint as IPEndPoint;
        }

        public void AcceptSSL(int Port, string CertificateFile) {
            throw new NotImplementedException();

            //if (!Listeners.ContainsKey(Port))
                //Listeners.Add(Port, new Tcp.Server(IPAddress.Any, Port, CertificateFile));
        }

        public void Decline(int Port) {
            Listeners[Port]?.Stop();
            Listeners.Remove(Port);
        }

        public virtual void Start() {
            Active = true;

            foreach (var Listener in Listeners.Values) {
                if (!Listener.Running) {
                    Listener.OnClientConnected += OnClientConnect;
                    Listener.Start();
                }
            }
        }

        public virtual void Stop() {
            Active = false;

            foreach (var Listen in Listeners.Values) {
                Listen.Stop();
            }
        }
    }
}
