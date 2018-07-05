using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Poly.Net {
    using Http;

    public partial class HttpServer {
        private async void OnClientConnect(TcpClient client) {
            await Task.Yield();
            client_connected();

            var connection = new Http.V1.Connection(client);
            var context = new Context(connection);

            while (Running && client.Connected) {
                client_began_reading(context);
                var read = await connection.ReadRequest(context.Request, context.Cancellation.Token);

                client_ended_reading(context);
                if (!read) break;

                client_began_processing(context);
                await handle_request(context);
                client_ended_processing(context);

                client_began_writing(context);
                var write = await connection.WriteResponse(context.Response, context.Cancellation.Token);

                client_ended_writing(context);
                if (!write) break;

                request_completed(context);
            }

            context.Cancellation.Cancel();
            context.Connection.Client.Close();
            client_disconnected();
        }
    }
}