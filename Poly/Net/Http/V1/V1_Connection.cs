using System.Net;

using Poly.Net.Tcp;

namespace Poly.Net.Http.V1
{
    public class Connection : IConnectionInterface
    {
        public Connection(TcpClient client)
        {
            Client = client;
        }

        public TcpClient Client { get; private set; }

        public async ValueTask Close()
        {
            await Task.Yield();

            Client?.Close();
        }

        public ValueTask<bool> Open(EndPoint endPoint)
        {
            if (Client is null)
                Client = new TcpClient();

            return Client.Connect(endPoint);
        }

        public ValueTask<bool> ReadRequest(RequestInterface request, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public ValueTask<bool> ReadResponse(ResponseInterface response, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public ValueTask<bool> WriteRequest(RequestInterface request, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public ValueTask<bool> WriteResponse(ResponseInterface response, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}