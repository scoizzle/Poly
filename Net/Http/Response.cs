using System.IO;

namespace Poly.Net.Http {

    public class Response {
        public Result Status;

        public ResponseHeaders Headers;

        public Stream Body;

        public Response(Result status) {
            Status = status;
            Headers = new ResponseHeaders();
            Headers.ContentLength = 0;
        }

        public Response(Result status, Stream body) : this(status) {
            Body = body;
            Headers.ContentLength = Body.Length;
        }
    }
}