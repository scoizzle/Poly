using System.Net;

namespace Poly.Net.Http
{
    public interface IConnectionInterface
    {
        ValueTask<bool> Open(EndPoint endPoint);
        ValueTask Close();

        ValueTask<bool> ReadRequest(RequestInterface request, CancellationToken cancellationToken = default);

        ValueTask<bool> ReadResponse(ResponseInterface response, CancellationToken cancellationToken = default);

        ValueTask<bool> WriteRequest(RequestInterface request, CancellationToken cancellationToken = default);

        ValueTask<bool> WriteResponse(ResponseInterface response, CancellationToken cancellationToken = default);
    }
}