using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net {
    using Data;
    using Http;

    public partial class HttpServer {
        internal int reading, writing, processing;
        internal int active { get => reading + writing + processing; }
        
        private void OnClientConnect(TcpClient client) {
            var connection = new Http.V1.Connection(client);
            var context = new Context(connection);
            
            BeginReadRequest(connection, context);
        }

        private void Disconnect(Context context) {
            context.Cancellation.Cancel();
            context.Connection.Client.Close();
        }

        private void BeginReadRequest(Connection connection, Context context) {
            Interlocked.Increment(ref reading);
            
            connection
                .ReadRequestAsync(context.Request, context.Cancellation.Token)
                .ContinueWith(_ => EndReadRequest(connection, context, _));
        }

        private void EndReadRequest(Connection connection, Context context, Task<bool> read_request) {
            Interlocked.Decrement(ref reading);

            if (read_request.CatchException() || !read_request.Result)
                Disconnect(context);
            
            ProcessRequest(connection, context);
        }

        private void ProcessRequest(Connection connection, Context context) {
            Interlocked.Increment(ref processing);

            handle_request(context).
                ContinueWith(_ => BeginSendResponse(connection, context));
        }

        private void BeginSendResponse(Connection connection, Context context) {
            Interlocked.Decrement(ref processing);
            Interlocked.Increment(ref writing);

            connection
                .WriteResponseAsync(context.Response, context.Cancellation.Token)
                .ContinueWith(_ => EndSendResponse(connection, context, _));
        }

        private void EndSendResponse(Connection connection, Context context, Task<bool> send_response) {
            Interlocked.Decrement(ref writing);

            if (send_response.CatchException() || !send_response.Result) {
                Disconnect(context);
            }
            else {
                context.Reset();
                BeginReadRequest(connection, context);
            }
        }
    }
}