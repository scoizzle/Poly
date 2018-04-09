using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Poly.Net {

    public class TcpServer : TcpListener {
        public TcpServer(int port) : this(IPAddress.Any, port) { }

        public TcpServer(IPAddress addr, int port) : base(addr, port) { }

        public Action<TcpClient> OnAcceptClient { get; set; }

        public bool Running { get => Active; }

        new public bool Start() {
            if (Active) return true;

            try {
                base.Start();
                StartAcceptSocket();
                Log.Debug($"Now listening on port {LocalEndpoint}");
            }
            catch (Exception Error) {
                Log.Error($"Couldn't begin accepting connections on port {LocalEndpoint}");
                Log.Error(Error);
                return false;
            }

            return true;
        }

        new public void Stop() {
            Log.Debug($"Shutting down on port {LocalEndpoint}");
            base.Stop();
        }
        
        private void StartAcceptSocket() {
            AcceptSocketAsync().ContinueWith(EndAcceptSocket);
        }

        private void EndAcceptSocket(Task<Socket> accept_socket) {
            if (accept_socket.IsFaulted)
                return;

            if (accept_socket.IsCompleted) {
                var client = new TcpClient(accept_socket.Result);
                OnAcceptClient(client);

                if (Active) {
                    StartAcceptSocket();
                }
            }
        }
    }
}