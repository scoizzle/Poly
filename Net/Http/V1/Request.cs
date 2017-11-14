using System;
using System.IO;

namespace Poly.Net.Http.V1 {
    using Data;
    using HeaderCollection = Data.KeyValueCollection<string>;

    public class Request : Http.Request {
        public Http.Connection Connection { get; set; }

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

        public string Method { get; set; }
        public string Target { get; set; }

        HeaderCollection.KeyValuePair authority;
        public string Authority {
            get { return authority.Value; }
            set { authority.Value = value; }
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

        HeaderCollection.CachedValue<long> content_length;
        public long ContentLength {
            get { return content_length.Value; }
            set { content_length.Value = value; }
        }

        public Stream Body { get; set; }

        public HeaderCollection Headers { get; set; }
        public JSON Arguments { get; set; }

        public Request(Http.Connection connection, string method, string target, HeaderCollection headers, Stream body) {
            Connection = connection;
			Method = method;
			Target = target;
			Headers = headers;

            authority = headers.GetStorage("Host");
            content_type = headers.GetStorage("Content-Type");
            content_encoding = headers.GetStorage("Content-Encoding");
            content_length = headers.GetCachedStorage(
                "Content-Length",
                long.TryParse,
                (long l, out string str) => {
                    str = l.ToString();
                    return true;
                });
            
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

			Arguments = new JSON();
			content_length.Value = body?.Length ?? 0;
        }
    }
}