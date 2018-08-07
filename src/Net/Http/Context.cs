using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net {
    using Http;

    public class Context {
        public readonly Connection Connection;

        public readonly Request Request;
        public readonly Response Response;

        public readonly Dictionary<object, object> Items;

        public readonly PerformanceTimer Timer;

        public Context(Connection connection) {
            Connection = connection;

            Timer = new PerformanceTimer();
            Request = new Request(Timer);
            Response = new Response(Timer);

            Items = new Dictionary<object, object>();
            Cancellation = new CancellationTokenSource();
        }

        public CancellationTokenSource Cancellation { get; private set; }

        public Task<bool> ReadRequest() =>
            Connection.ReadRequest(Request, Cancellation.Token);

        public Task<bool> ReadRequestPayload() =>
            Connection.ReadRequestPayload(Request, Cancellation.Token);

        public Task<bool> ReadResponse() =>
            Connection.ReadResponse(Response, Cancellation.Token);

        public Task<bool> ReadResponsePayload() =>
            Connection.ReadResponsePayload(Response, Cancellation.Token);

        public Task<bool> WriteRequest() =>
            Connection.WriteRequest(Request, Cancellation.Token);

        public Task<bool> WriteResponse() =>
            Connection.WriteResponse(Response, Cancellation.Token);

        public void Reset() {
            Request.Reset();
            Response.Reset();
            
            Cancellation = new CancellationTokenSource();

            Items.Clear();
            Timer.Clear();
        }
    }
}