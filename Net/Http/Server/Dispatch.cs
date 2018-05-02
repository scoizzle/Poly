using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Poly.Net {
    using Http;

    public partial class HttpServer {
        private void OnClientConnect(TcpClient client) {
            var connection = new Http.V1.Connection(client);
            var context = new Context(connection);

            client_connected();
            BeginReadRequest(context);
        }

        private void Disconnect(Context context) {
            context.Cancellation.Cancel();
            context.Connection.Client.Close();
            client_disconnected();
        }

        private void BeginReadRequest(Context context) {
            client_began_reading(context);

            context.Connection
                .ReadRequestAsync(context.Request, context.Cancellation.Token)
                .ContinueWith(_ => EndReadRequest(context, _));
        }

        private void EndReadRequest(Context context, Task<bool> read_request) {
            client_ended_reading(context);

            if (read_request.CatchException() || !read_request.Result)
                Disconnect(context);
            else
                ProcessRequest(context);
        }

        private void ProcessRequest(Context context) {
            client_began_processing(context);

            handle_request(context).
                ContinueWith(_ => BeginSendResponse(context));
        }

        private void BeginSendResponse(Context context) {
            client_ended_processing(context);
            client_began_writing(context);

            context.Connection
                .WriteResponseAsync(context.Response, context.Cancellation.Token)
                .ContinueWith(_ => EndSendResponse(context, _));
        }

        private void EndSendResponse(Context context, Task<bool> send_response) {
            client_ended_writing(context);

            if (send_response.CatchException() || !send_response.Result) {
                Disconnect(context);
            }
            else {
                request_completed(context);

                BeginReadRequest(context);
            }
        }
    }
}