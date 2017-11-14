using System;
using System.IO;

namespace Poly.Net.Http {
    using HeaderCollection = Data.KeyValueCollection<string>;

	public interface Response {
        Connection Connection { get; set; }

        DateTime Date { get; set; }
        DateTime LastModified { get; set; }

		Result Status { get; set; }

        string ContentType { get; set; }
        string ContentEncoding { get; set; }
        string Expires { get; set; }
        string TransferEncoding { get; set; }
        long ContentLength { get; set; }

        Stream Body { get; set; }
        HeaderCollection Headers { get; set; }
    }
}