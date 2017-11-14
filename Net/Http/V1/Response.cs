using System;
using System.IO;

namespace Poly.Net.Http.V1 {
    using HeaderCollection = Data.KeyValueCollection<string>;

	public class Response : Http.Response {
		public Http.Connection Connection { get; set; }

		public Result Status { get; set; }

        HeaderCollection.CachedValue<DateTime> date;
        public DateTime Date {
            get { return date.Value; }
            set { date.Value = value; }
        }

        HeaderCollection.CachedValue<DateTime> last_modified;
        public DateTime LastModified {
            get { return last_modified.Value; }
            set { last_modified.Value = value; }
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

        HeaderCollection.KeyValuePair expires;
        public string Expires {
            get { return expires.Value; }
            set { expires.Value = value; }
        }

        HeaderCollection.KeyValuePair transfer_encoding;
        public string TransferEncoding {
            get { return transfer_encoding.Value; }
            set { transfer_encoding.Value = value; }
        }

        HeaderCollection.CachedValue<long> content_length;
        public long ContentLength {
            get { return content_length.Value; }
            set { content_length.Value = value; }
        }

        public Stream Body { get; set; }
        public HeaderCollection Headers { get; set; }

        public Response(Http.Connection connection, Result status, HeaderCollection headers, Stream body) {
            Connection = connection;
            Status = status;
            Headers = headers;
            Body = body;

            content_type = headers.GetStorage("Content-Type");
            content_encoding = headers.GetStorage("Content-Encoding");
            expires = headers.GetStorage("Expires");
            transfer_encoding = headers.GetStorage("Transfer-Encoding");


            date = headers.GetCachedStorage(
                "Date",
                (string str, out DateTime time) => {
                    time = str.FromHttpTimeString();
                    return true;
                },
                (DateTime time, out string str) => {
                    str = time.ToHttpTimeString();
                    return true;
                });

            last_modified = headers.GetCachedStorage(
                "Last-Modified",
                (string str, out DateTime time) => {
                    time = str.FromHttpTimeString();
                    return true;
                },
                (DateTime time, out string str) => {
                    str = time.ToHttpTimeString();
                    return true;
                });
            
            content_length = headers.GetCachedStorage(
                "Content-Length",
                long.TryParse,
                (long l, out string str) => {
                    str = l.ToString();
                    return true;
                });

            content_length.Value = body?.Length ?? 0;
        }
    }
}