using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Poly.Net {
    using IO;

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

        public int Available => In.Available + Socket.Available;
        
        public bool Connected => Socket?.Connected == true;
        
        public bool HasDataAvailable => Available > 0; 

        public Task<bool> Connect(EndPoint ep) {
            var completion_source = new TaskCompletionSource<bool>();

            Socket.ConnectAsync(ep).ContinueWith(connect_async => {
                if (connect_async.CatchException())
                    completion_source.SetResult(false);

                completion_source.SetResult(Socket.Connected);
            });

            return completion_source.Task;
        }

        public async Task<bool> Connect(string host, int port) {
            try {
                var host_addresses = await Dns.GetHostAddressesAsync(host);

                foreach (var address in host_addresses) {
                    var connect = await Connect(new IPEndPoint(address, port));

                    if (connect)
                        return true;
                }

                return false;
            }
            catch (Exception error) { 
                Log.Debug(error);
            }

            return false;
        }
    }
}