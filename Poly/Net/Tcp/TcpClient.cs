using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Poly.Net.Tcp {
    using IO;

    public partial class TcpClient : MemoryBufferedStream {
        public TcpClient() {
            Socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        public TcpClient(Socket socket) : base(new NetworkStream(socket)) {
            Socket = socket;
            Socket.NoDelay = true;
        }

        ~TcpClient() {
            Socket?.Dispose();
        }

        public Socket Socket { get; protected set; }

        public IPEndPoint LocalIPEndPoint => Socket?.LocalEndPoint as IPEndPoint;
        
        public IPEndPoint RemoteIPEndPoint => Socket?.RemoteEndPoint as IPEndPoint;

        public int Available => In.Available + Socket.Available;
        
        public bool Connected => Socket?.Connected == true;
        
        public bool HasDataAvailable => Available > 0; 

        public async Task<bool> Connect(EndPoint ep) {
            try {
                await Socket.ConnectAsync(ep);
                Stream = new NetworkStream(Socket);
                return true;
            }
            catch (Exception error) {
                Log.Error(error);
                return false;
            }
        }

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