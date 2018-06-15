using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Poly.Net {
    public partial class TcpServer {
        TcpListener listener;

        public TcpServer(int port) : this(IPAddress.Any, port) { }

        public TcpServer(IPAddress addr, int port) { 
            listener = new TcpListener(addr, port);
        }

        public bool Active { get; private set; }
        
        public Action<TcpClient> OnAcceptClient { get; set; }

        public EndPoint LocalEndpoint { get => listener?.LocalEndpoint; }

        public bool Start() {
            if (Active) return true;

            try {
                listener.Start();
                StartAcceptSocket();
                Log.Debug($"Now listening on port {LocalEndpoint}");

                return Active = true;
            }
            catch (Exception Error) {
                Log.Error($"Couldn't begin accepting connections on port {LocalEndpoint}");
                Log.Error(Error);

                return Active = false;
            }
        }

        public void Stop() {
            Log.Debug($"Shutting down on port {LocalEndpoint}");
            listener.Stop();
            Active = false;
        }
        
        private void StartAcceptSocket() {
            listener.AcceptSocketAsync().ContinueWith(EndAcceptSocket);
        }

        private void EndAcceptSocket(Task<Socket> accept_socket) {
            if (accept_socket.CatchException())
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