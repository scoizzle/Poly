using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;


namespace Poly.Net.Tcp {
    public class Server : TcpListener {
        public int Port { get; private set; }

        public bool Running { get { return Active; } }

        public delegate void OnClientConnectDelegate(Client Client);
        public event OnClientConnectDelegate ClientConnected;

        public Server(int port) : this(IPAddress.Any, port) {  }        
        public Server(IPAddress addr, int port) : base(addr, port) { Port = port; }

        new public bool Start() {
            if (Active)
                return true;

            if (ClientConnected == null) {
                App.Log.Error("Can't accept connections without OnClientConnect specified!");
                return false;
            }

            try {
                Start(65536);

				App.Log.Info("Now listening on port {0}", Port);
            }
            catch {
                App.Log.Error("Couldn't begin accepting connections on port {0}", Port);
                return false;
            }

            AcceptConnections();
            return true;
        }

        new public void Stop() {
            base.Stop();
        }

		private async void AcceptConnections() {
			try {
                do {
                    var socket = await AcceptSocketAsync();

                    OnClientConnect(socket);
                }
                while (Active);
            }
			catch { }

            if (Active)
                AcceptConnections();
		}

        private async void OnClientConnect(Socket sock) {
            await Task.Yield();

            ClientConnected(sock);
        }
    }
}
