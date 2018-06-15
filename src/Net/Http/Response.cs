using System.IO;
using System.Diagnostics;

namespace Poly.Net.Http {
    public class Response {
        public Status Status;

        public ResponseHeaders Headers;

        public Stream Body;

        public PerformanceCounter Timer { get; private set; }

        public Response() : this(Status.NotFound, new PerformanceCounter()) { }

        public Response(PerformanceCounter timer) : this(Status.NotFound, timer) { }

        public Response(Status status) : this(status, new PerformanceCounter()) { }

        public Response(Status status, PerformanceCounter timer) {
            Status = status;
            Timer = timer;
            Headers = new ResponseHeaders();
        }

        public Response(Status status, Stream body) : this(status, body, new PerformanceCounter()) { }

        public Response(Status status, Stream body, PerformanceCounter timer) : this(status, timer) {
            Body = body;
            Headers.ContentLength = Body.Length;
        }

        public void Reset() {
            Status = Status.NotFound;
            Headers.Reset();
            Body = null;
        }
    }
}