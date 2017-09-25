using System.IO;

namespace Poly.Net.Http {
    using HeaderCollection = Data.KeyValueCollection<string>;

    public interface Response {
        Result Status { get; set; }

        string Date { get; set; }
        string ContentType { get; set; }
        string ContentEncoding { get; set; }
        string TransferEncoding { get; set; }
        string LastModified { get; set; }
        long ContentLength { get; set; }
        Stream Body { get; set; }
        HeaderCollection Headers { get; set; }
    }
}