using System.IO;

namespace Poly.Net.Http.V1 {
    using Data;
    using HeaderCollection = Data.KeyValueCollection<string>;

    public class Request : Http.Request {
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
        public HeaderCollection Arguments { get; set; }
        
        public Request(string method, string target, HeaderCollection headers) {
            authority = headers.GetStorage("Host");
            content_type = headers.GetStorage("Content-Type");
            content_encoding = headers.GetStorage("Content-Encoding");
            last_modified = headers.GetStorage("last-modified");
            content_length = headers.GetCachedStorage(
                "Content-Length",
                long.TryParse,
                (long l, out string str) => {
                    str = l.ToString();
                    return true;
                });

			Method = method;
			Target = target;
			Headers = headers;

            Arguments = new KeyValueCollection<string>();
        }
    }
}