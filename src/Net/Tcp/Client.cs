using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Poly.Net.Tcp {
    using IO;

    public partial class Client : MemoryBufferedStream {
        public Client() {
            Socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        public Client(Socket socket) : base(new NetworkStream(socket)) {
            Socket = socket;
            Socket.NoDelay = true;
        }

        public Client(EndPoint ep) : this() {
            Connect(ep);
        }

        public Client(IPAddress ip, int port) : this() {
            Connect(ip, port);
        }

        public Client(string host_name, int port) : this() {
            Connect(host_name, port);
        }

        ~Client() {
            Socket?.Dispose();
        }

        public bool Secure { get; private set; }

        public Socket Socket { get; protected set; }

        public IPEndPoint LocalIPEndPoint { get { return Socket?.LocalEndPoint as IPEndPoint; } }
        public IPEndPoint RemoteIPEndPoint { get { return Socket?.RemoteEndPoint as IPEndPoint; } }

        public int Available => In.Available + Socket.Available;
        
        public bool Connected => Socket?.Connected == true;
        
        public bool HasDataAvailable => Available > 0; 

        public Task<bool> Connect(EndPoint ep) =>
            Socket.ConnectAsync(ep)
                .ContinueWith(connect => {
                    if (connect.CatchException() || !Socket.Connected)
                        return false;
                        
                    Stream = new NetworkStream(Socket);
                    return true;
                });

        public Task<bool> Connect(IPAddress ip, int port) =>
            Connect(new IPEndPoint(ip, port));

        public Task<bool> Connect(string host_name, int port) =>
            Connect(Dns.GetHostAddressesAsync(host_name), port);

        private async Task<bool> Connect(Task<IPAddress[]> dns, int port) {
            foreach (var addr in await dns)
                if (await Connect(addr, port))
                    return true;

            return false;
        }
    }
}