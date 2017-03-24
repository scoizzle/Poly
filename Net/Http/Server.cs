using System;
using System.Threading;

namespace Poly.Net.Http {
    public partial class Server : Host {
        public int Port { get; set; } = 80;
        public int MaxConcurrentClients { get; set; } = Environment.ProcessorCount * 1024;
        public int ClientReceiveTimeout { get; set; } = 5000;
        public int ClientSendTimeout { get; set; } = 5000;

        public bool Running { get { return Listener?.Running == true; } }

        private Tcp.Server Listener;
        private SemaphoreSlim sem;

        public Server(string hostname) : base(hostname) { }

        public Server(string hostname, int port) : base(hostname) {
            Port = port;
        }

        ~Server() { 
            Stop();
        }

        private void Init() {
            Listener = new Tcp.Server(Port);
            Listener.OnClientConnected += OnClientConnected;
            sem = new SemaphoreSlim(Environment.ProcessorCount);
        }

        public virtual bool Start() {
            if (Listener == null) Init();

            Ready();
            return Listener.Start();
        }

        public virtual void Stop() {
            Listener?.Stop();
            Listener = null;
        }

        public virtual void Restart() {
            Stop();
            Start();
        }
    }
}
