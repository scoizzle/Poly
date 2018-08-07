using System.Threading.Tasks;

namespace Poly.Net.Http.V2 {

    public class Connection {

        public Http.Request ReadRequest() {
            return null;
        }

        public Http.Response ReadResponse() {
            return null;
        }

        public bool WriteRequest(Http.Request request) {
            return false;
        }

        public bool WriteResponse(Http.Response response) {
            return false;
        }

        public Task<Http.Request> ReadRequestAsync() {
            return Task.FromResult(default(Http.Request));
        }

        public Task<Http.Response> ReadResponseAsync() {
            return Task.FromResult(default(Http.Response));
        }

        public Task<bool> WriteRequestAsync(Http.Request request) {
            return Task.FromResult(false);
        }

        public Task<bool> WriteResponseAsync(Http.Response response) {
            return Task.FromResult(false);
        }
    }
}