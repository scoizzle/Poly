using System.IO;

namespace Poly.Net.Http {
    using Data;

    public interface Request {
        string Method { get; set; }
        string Target { get; set; }
        string Authority { get; set; }
        string ContentType { get; set; }
        string ContentEncoding { get; set; }
        string LastModified { get; set; }
        long ContentLength { get; set; }

        Stream Body { get; set; }

        KeyValueCollection<string> Headers { get; set; }
        KeyValueCollection<string> Arguments { get; set; }
    }
}