using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    public interface Connection {
        TcpClient Client { get; set; }

        Task<bool> ReadRequest(Request request, CancellationToken cancellation_token);

        Task<bool> ReadRequestPayload(Request request, CancellationToken cancellation_token);

        Task<bool> ReadResponse(Response response, CancellationToken cancellation_token);

        Task<bool> ReadResponsePayload(Response response, CancellationToken cancellation_token);

        Task<bool> WriteRequest(Request request, CancellationToken cancellation_token);

        Task<bool> WriteResponse(Response response, CancellationToken cancellation_token);
    }

    public static class ConnectionExtensions {
        public static Task<bool> ReadRequest(this Connection connection, Request request) =>
            connection.ReadRequest(request, CancellationToken.None);

        public static Task<bool> ReadRequestPayload(this Connection connection, Request request) =>
            connection.ReadRequestPayload(request, CancellationToken.None);

        public static Task<bool> ReadResponse(this Connection connection, Response response) =>
            connection.ReadResponse(response, CancellationToken.None);

        public static Task<bool> ReadResponsePayload(this Connection connection, Response response) =>
            connection.ReadResponsePayload(response, CancellationToken.None);

        public static Task<bool> WriteRequest(this Connection connection, Request request) =>
            connection.WriteRequest(request, CancellationToken.None);

        public static Task<bool> WriteResponse(this Connection connection, Response response) =>
            connection.WriteResponse(response, CancellationToken.None);
    }
}