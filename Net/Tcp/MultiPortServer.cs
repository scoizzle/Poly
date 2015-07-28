using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Tcp {
    public class MultiPortServer {
        public Event.Engine Events = new Event.Engine();
        public Dictionary<int, Tcp.Server> Listeners = new Dictionary<int, Tcp.Server>();

        public event Tcp.Server.ClientConnectHandler OnClientConnect;
        public event Action OnStart, OnStop;

        public bool Active { get; private set; }

        public void Listen(int Port) {
            if (!Listeners.ContainsKey(Port))
                Listeners.Add(Port, new Tcp.Server(Port));
        }

        public void ListenSsl(int Port, string CertificateFile) {
            if (!Listeners.ContainsKey(Port))
                Listeners.Add(Port, new Tcp.Server(IPAddress.Any, Port, CertificateFile));
        }

        public void Start() {
            Active = true;

            if (OnStart != null) 
                OnStart();

            foreach (var Listener in Listeners.Values) {
                if (!Listener.Active) {
                    Listener.ClientConnect += OnClientConnect;
                    Listener.Start();
                }
            }
        }

        public void Stop() {
            Active = false;

            if (OnStop != null) 
                OnStop();

            foreach (var Listen in Listeners.Values) {
                Listen.Stop();
            }
        }
    }
}
