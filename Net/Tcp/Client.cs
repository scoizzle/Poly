using Poly.Data;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Poly.Net {

    public partial class TcpClient : MemoryBufferedStream {
        public TcpClient(Socket socket) : base(new NetworkStream(socket)) {
            Socket = socket;
            Socket.NoDelay = true;
        }

        ~TcpClient() {
            Socket?.Dispose();
        }

        public bool Secure { get; private set; }

        public Socket Socket { get; protected set; }

        public IPEndPoint LocalIPEndPoint { get { return Socket?.LocalEndPoint as IPEndPoint; } }
        public IPEndPoint RemoteIPEndPoint { get { return Socket?.RemoteEndPoint as IPEndPoint; } }

        public int Available { get => In.Available + Socket.Available; }

        public bool Connected {
            get { return Socket?.Connected == true; }
        }

        public async Task<bool> Connect(EndPoint ep) {
            try {
                await Socket.ConnectAsync(ep);

                if (Socket.Connected) {
                    Stream = new NetworkStream(Socket);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public async Task<bool> Connect(string host, int port) {
            try {
                var get_addresses = Dns.GetHostAddressesAsync(host);
                var addresses = await get_addresses;

                if (addresses.Length == 0) return false;
                var get_connection = Connect(
                    new IPEndPoint(
                        addresses.First(),
                        port
                ));

                return await get_connection;
            }
            catch { }

            return false;
        }

        public Task<bool> Connect(IPAddress addr, int port) {
            return Connect(new IPEndPoint(addr, port));
        }
    }
}