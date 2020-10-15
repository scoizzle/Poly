using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {    
    public interface ConnectionInterface {
        ValueTask<bool> Open();
        ValueTask Close();
        
        ValueTask<bool> ReadRequest(RequestInterface request, CancellationToken cancellationToken = default);

        ValueTask<bool> ReadResponse(ResponseInterface response, CancellationToken cancellationToken = default);

        ValueTask<bool> WriteRequest(RequestInterface request, CancellationToken cancellationToken = default);

        ValueTask<bool> WriteResponse(ResponseInterface response, CancellationToken cancellationToken = default);
    }
}