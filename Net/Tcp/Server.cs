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

        private Thread ConnectionAccepter;

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

            ConnectionAccepter = GetAcceptThread();
            ConnectionAccepter.Start();

            return true;
        }

        new public void Stop() {
            ConnectionAccepter.Abort();
            base.Stop();
        }

        private Thread GetAcceptThread() {
            return new Thread(AcceptConnections);
        }

		private void AcceptConnections() {
			while (Active) {
				var socket = AcceptSocket();

				Task.Run(() => {
					ClientConnected(socket);
				});
			}
		}
    }
}
