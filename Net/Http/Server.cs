using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Http {
    using Data;
    using Net.Tcp;
    using Script;

    public partial class Server : Host {
        public int Port { get; set; } = 80;
        public int MaxConcurrentClients { get; set; } = Environment.ProcessorCount;
        public int SendBufferSize { get; set; } = 16384;

        public bool Running { get { return Listener?.Running == true; } }

        private Tcp.Server Listener;
        private CancellationTokenSource Cancel;

        public Server(string hostname) : base(hostname) { }

        public Server(string hostname, int port) : base(hostname) {
            Port = port;
        }

        private void Init() {
            Listener = new Tcp.Server(Port);
            Listener.OnClientConnected += OnClientConnected;

            Cancel = new CancellationTokenSource();
        }

        private void Dispose() {
            Listener?.Stop();
            Cancel?.Cancel();
            Cancel = null;
            Listener = null;
        }

        public virtual void Start() {
            if (Listener == null) Init();

            Ready();
            Listener.Start();
        }

        public virtual void Stop() {
            Dispose();
            Cache.Dispose();
        }

        public virtual void Restart() {
            Dispose();
            Init();
            Listener.Start();
        }

        public void Mime(string Ext, string Mime) {
            Poly.Mime.List[Ext] = Mime;
        }
    }
}
