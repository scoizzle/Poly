using System.IO;
using System.Diagnostics;

namespace Poly.Net.Http {

    public class Request {
        public string Method, Path, Authority, Scheme;
        public RequestHeaders Headers;

        public PerformanceCounter Timer { get; private set; }

        public Stream Body;

        public Request() : this(new PerformanceCounter()) { }

        public Request(PerformanceCounter timer) {
            Headers = new RequestHeaders();
            Timer = timer;
        }

        public Request(Stream body) : this() {
            Body = body;
            Headers.ContentLength = body.Length;
        }

        public void Reset() {
            Method = Path = Authority = Scheme = null;
            Headers.Reset();
        }
    }
}