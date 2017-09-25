using System.IO;

namespace Poly.Net.Http.V1 {
    using HeaderCollection = Data.KeyValueCollection<string>;

    public class Response : Http.Response {
        public Result Status { get; set; }

        HeaderCollection.KeyValuePair date;
        public string Date {
            get { return date.Value; }
            set { date.Value = value; }
        }

        HeaderCollection.KeyValuePair content_type;
        public string ContentType {
            get { return content_type.Value; }
            set { content_type.Value = value; }
        }

        HeaderCollection.KeyValuePair content_encoding;
        public string ContentEncoding {
            get { return content_encoding.Value; }
            set { content_encoding.Value = value; }
        }

        HeaderCollection.KeyValuePair transfer_encoding;
        public string TransferEncoding {
            get { return transfer_encoding.Value; }
            set { transfer_encoding.Value = value; }
        }

        HeaderCollection.KeyValuePair last_modified;
        public string LastModified {
            get { return last_modified.Value; }
            set { last_modified.Value = value; }
        }

        HeaderCollection.CachedValue<long> content_length;
        public long ContentLength {
            get { return content_length.Value; }
            set { content_length.Value = value; }
        }

        public Stream Body { get; set; }
        public HeaderCollection Headers { get; set; }

        public Response(Result status, HeaderCollection headers) {
            Status = status;
            Headers = headers;

            date = headers.GetStorage("Date");
            content_type = headers.GetStorage("Content-Type");
            content_encoding = headers.GetStorage("Content-Encoding");
            transfer_encoding = headers.GetStorage("Transfer-Encoding");
            last_modified = headers.GetStorage("Last-Modified");

            content_length = headers.GetCachedStorage(
                "Content-Length",
                long.TryParse,
                (long l, out string str) => {
                    str = l.ToString();
                    return true;
                });

            content_length.Value = 0;
        }
    }
}