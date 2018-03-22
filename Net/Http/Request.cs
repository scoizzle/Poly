using System.IO;

namespace Poly.Net.Http {

    public class Request {
        public string Method, Path, Authority, Scheme;
        public HeaderStorage Headers;

        public Stream Body;

        public Request() {
            Headers = new HeaderStorage();
            Headers.ContentLength = 0;
        }

        public Request(Stream body) : this() {
            Body = body;
            Headers.ContentLength = body.Length;
        }
    }
}