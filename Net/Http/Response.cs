using System.IO;
using System.Diagnostics;

namespace Poly.Net.Http {
    public class Response {
        public Result Status;

        public ResponseHeaders Headers;

        public Stream Body;

        public PerformanceCounter Timer { get; private set; }

        public Response() : this(Result.NotFound, new PerformanceCounter()) { }

        public Response(PerformanceCounter timer) : this(Result.NotFound, timer) { }

        public Response(Result status) : this(status, new PerformanceCounter()) { }

        public Response(Result status, PerformanceCounter timer) {
            Status = status;
            Timer = timer;
            Headers = new ResponseHeaders();
        }

        public Response(Result status, Stream body) : this(status, body, new PerformanceCounter()) { }

        public Response(Result status, Stream body, PerformanceCounter timer) : this(status, timer) {
            Body = body;
            Headers.ContentLength = Body.Length;
        }

        public void Reset() {
            Status = Result.NotFound;
            Headers.Reset();
            Body = null;
        }
    }
}