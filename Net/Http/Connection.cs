using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    public interface Connection {
        TcpClient Client { get; set; }

        bool Connected { get; }
        bool HasDataAvailable { get; }

        Task<bool> ReadRequestAsync(Request request, CancellationToken cancellation_token);
        Task<bool> ReadResponseAsync(Response response, CancellationToken cancellation_token);

        Task<bool> WriteRequestAsync(Request request, CancellationToken cancellation_token);
        Task<bool> WriteResponseAsync(Response response, CancellationToken cancellation_token);
    }

    public static class ConnectionExtensions {
        public static Task<bool> ReadRequestAsync(this Connection connection, Request request) =>
            connection.ReadRequestAsync(request, CancellationToken.None);

        public static Task<bool> ReadResponseAsync(this Connection connection, Response response) =>
            connection.ReadResponseAsync(response, CancellationToken.None);

        public static Task<bool> WriteRequestAsync(this Connection connection, Request request) =>
            connection.WriteRequestAsync(request, CancellationToken.None);

        public static Task<bool> WriteResponseAsync(this Connection connection, Response response) =>
            connection.WriteResponseAsync(response, CancellationToken.None);
    }
}