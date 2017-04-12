using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;


namespace Poly.Net.Tcp {
    public class Server : TcpListener {
        public int Port { get; private set; }
        public bool Running { get { return Active; } }

        public delegate Task OnClientConnectDelegate(Client Client);
        public event OnClientConnectDelegate OnClientConnected;

        public Task ListenerTask { get; private set; }

        public Server(int port) : this(IPAddress.Any, port) { }
        public Server(IPAddress addr, int port) : base(addr, port) { Port = port; }

        new public bool Start() {
            if (Active) return true;

            if (OnClientConnected == null) {
                Log.Error("Can't accept connections without OnClientConnect specified!");
                return false;
            }

            try {
                base.Start();

                Log.Debug("Now listening on port {0}", Port);
            }
            catch (Exception Error) {
                Log.Error("Couldn't begin accepting connections on port {0}", Port);
                Log.Error(Error);
                return false;
            }
        
            ListenerTask = StartAcceptTask();
            return true;
        }

        new public void Stop() {
            Server.Dispose();
            base.Stop();
        }

        async Task StartAcceptTask() {
			Task lastStarted;
            while (Active) {
                try { lastStarted = OnClientConnected(new Client(await AcceptSocketAsync())); }
                catch (OperationCanceledException) { }
                catch (SocketException) { }

                await Task.Delay(0);
            }
        }
    }
}
