using System;
using System.IO;

namespace Poly.Net.Http {
    using Data;

    public interface Request {
        Connection Connection { get; set; }

        DateTime Date { get; set; }
        DateTime LastModified { get; set; }

        string Method { get; set; }
        string Target { get; set; }
        string Authority { get; set; }
        string ContentType { get; set; }
        string ContentEncoding { get; set; }
        long ContentLength { get; set; }

        Stream Body { get; set; }

        KeyValueCollection<string> Headers { get; set; }
        JSON Arguments { get; set; }
    }

	public static class RequestExtensions {
		public static Response Send(this Request This, Result status) {
			This.Connection.New(out Response response, status);
			return response;
		}

		public static Response Send(this Request This, Result status, Stream body) {
			This.Connection.New(out Response response, status, null, body);
			return response;
		}

		public static Response Send(this Request This, Result status, string content) {
            return Send(This, status, content.GetStream());
		}

        public static Response Send(this Request This, Result status, Action<Response> modifier) {
			This.Connection.New(out Response response, status);
            modifier(response);
			return response;
		}
	}
}