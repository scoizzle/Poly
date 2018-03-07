using System.IO;

namespace Poly.Net.Http {

    public class Response {
        public Result Status;

        public Headers Headers;

        public Stream Body;

        public Response(Result status) {
            Status = status;
            Headers = new Headers();
            Headers.ContentLength = 0;
        }

        public Response(Result status, Stream body) : this(status) {
            Body = body;
            Headers.ContentLength = Body.Length;
        }
    }
}