using System.IO;
using System.Diagnostics;

namespace Poly.Net.Http {
    public class Response {
        public Status Status;

        public ResponseHeaders Headers;

        public Stream Body;

        public PerformanceTimer Timer { get; private set; }

        public Response() : this(Status.NotFound, new PerformanceTimer()) { }

        public Response(PerformanceTimer timer) : this(Status.NotFound, timer) { }

        public Response(Status status) : this(status, new PerformanceTimer()) { }

        public Response(Status status, PerformanceTimer timer) {
            Status = status;
            Timer = timer;
            Headers = new ResponseHeaders();
        }

        public Response(Status status, Stream body) : this(status, body, new PerformanceTimer()) { }

        public Response(Status status, Stream body, PerformanceTimer timer) : this(status, timer) {
            Body = body;
            Headers.ContentLength = body.Length;
        }

        public void Reset() {
            Status = Status.NotFound;
            Headers.Reset();
            
            if (Body is FileStream fs)
                fs.Close();

            Body = null;
        }
    }
}