using System.Net;
using System.Net.Sockets;
using System.Net.Security;

using Poly.IO;

namespace Poly.Net.Tcp
{
    public partial class TcpClient : MemoryBufferedStream
    {
        public TcpClient()
        {
            Socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        public TcpClient(Socket socket) : base(new NetworkStream(socket))
        {
            Socket = socket;
            Socket.NoDelay = true;
        }

        ~TcpClient()
        {
            Socket?.Dispose();
        }

        public Socket Socket { get; protected set; }

        public IPEndPoint LocalIPEndPoint => Socket?.LocalEndPoint as IPEndPoint;

        public IPEndPoint RemoteIPEndPoint => Socket?.RemoteEndPoint as IPEndPoint;

        public int Available => In.Count + Socket.Available;

        public bool Connected => Socket?.Connected == true;

        public bool HasDataAvailable => Available > 0;

        public bool IsSecure
            => Stream is SslStream sslStream
            && sslStream.IsAuthenticated
            && sslStream.IsEncrypted;

        public async ValueTask<bool> Connect(EndPoint ep)
        {
            try
            {
                await Socket.ConnectAsync(ep);
                Stream = new NetworkStream(Socket);
                return true;
            }
            catch (Exception error)
            {
                Log.Error(error);
                return false;
            }
        }

        public async ValueTask<bool> ConnectSsl(EndPoint endPoint, Func<SslStream, Task<bool>> authenticationDelegate)
        {
            try
            {
                await Socket.ConnectAsync(endPoint);

                var networkStream = new NetworkStream(Socket);
                var sslStream = new SslStream(networkStream);

                if (!await authenticationDelegate(sslStream))
                    return false;

                Stream = sslStream;
                return true;
            }
            catch (Exception error)
            {
                Log.Error(error);
                return false;
            }
        }

        public ValueTask<bool> Connect(IPAddress ip, int port) =>
            Connect(new IPEndPoint(ip, port));

        public ValueTask<bool> Connect(string host_name, int port) =>
            Connect(Dns.GetHostAddressesAsync(host_name), port);

        private async ValueTask<bool> Connect(Task<IPAddress[]> dns, int port)
        {
            foreach (var addr in await dns)
                if (await Connect(addr, port))
                    return true;

            return false;
        }
    }
}