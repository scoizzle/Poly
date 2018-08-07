using System.IO;
using System.Diagnostics;

namespace Poly.Net.Http {
    public class Request {
        public string Method, Path, Authority, Scheme;

        public readonly RequestHeaders Headers;
        public readonly PerformanceTimer Timer;
        
        public Stream Body;

        public Request() : this(new PerformanceTimer()) { }

        public Request(PerformanceTimer timer) {
            Headers = new RequestHeaders();
            Timer = timer;
        }

        public void Reset() {
            Method = Path = Authority = Scheme = null;
            Headers.Reset();
            
            if (Body is FileStream fs)
                fs.Close();
        }
    }
}