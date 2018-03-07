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

        private void Disconnect(Connection connection) {
            connection.Client.Dispose();
        }

        private void BeginReadRequest(Connection connection, Context context) {
            Interlocked.Increment(ref reading);
            
            connection
                .ReadRequestAsync(context.Request)
                .ContinueWith(_ => EndReadRequest(connection, context, _));
        }

        private void EndReadRequest(Connection connection, Context context, Task<bool> read_request) {
            Interlocked.Decrement(ref reading);

            if (read_request.IsFaulted) {
                HandleError(read_request);
                Disconnect(connection);
            }
            else
            if (read_request.IsCompleted && read_request.Result) {
                ProcessRequest(connection, context);
            }
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
                .WriteResponseAsync(context.Response)
                .ContinueWith(_ => EndSendResponse(connection, context, _));
        }

        private void EndSendResponse(Connection connection, Context context, Task<bool> send_response) {
            Interlocked.Decrement(ref writing);

            if (send_response.IsFaulted) {
                HandleError(send_response);
                Disconnect(connection);
            }
            else
            if (send_response.IsCompleted && send_response.Result) {
                context.Reset();
                BeginReadRequest(connection, context);
            }
        }

        private void HandleError(Task<bool> failed_task) {
            try {
                throw failed_task.Exception;
            }
            catch (Exception error) {
                Log.Debug(error.Message);
            }
        }
    }
}