using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Poly.Net {
    using Http;

    public partial class HttpServer {
        public class Context {
            public Connection Connection { get; private set; }
            public CancellationTokenSource Cancellation { get; private set; }

            public Request Request { get; private set; }
            public Response Response { get; private set; }

            public Dictionary<object, object> Items;

            public PerformanceTimer Timer { get; private set; }

            public Context(Connection connection) {
                Connection = connection;

                Timer = new PerformanceTimer();
                Request = new Request(Timer);
                Response = new Response(Timer);

                Items = new Dictionary<object, object>();
                Cancellation = new CancellationTokenSource();
            }

            public void Reset() {
                Request.Reset();
                Response.Reset();
                
                Cancellation = new CancellationTokenSource();

                Items.Clear();
                Timer.Clear();
            }
        }
    }
}