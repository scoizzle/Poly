using System.Collections.Generic;

namespace Poly.Net {
    using Http;

    public partial class HttpServer {
        public class Context {
            public Connection Connection { get; private set; }

            public Request Request { get; private set; }
            public Response Response { get; private set; }

            public Dictionary<object, object> Items;

            public Context(Connection connection) {
                Connection = connection;
                Items = new Dictionary<object, object>();
                Request = new Request();
                Response = new Response(Result.NotFound);
            }

            public void Reset() {
                Request = new Request();
                Response = new Response(Result.NotFound);
                Items.Clear();
                Connection.Client.RebaseBuffers();
            }
        }
    }
}