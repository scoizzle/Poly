using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;

    public partial class CacheModule : HttpServer.Module {
        class Item {
            public byte[] Content;
            public long ContentLength;
            public string ContentType;
            public DateTime LastModified;
            public DateTime Expires;

            public Item(Response to_cache) {
                Content = new byte[to_cache.Body.Length];
                to_cache.Body.Read(Content, 0, Content.Length);

                ContentType = to_cache.Headers.ContentType;
                ContentLength = to_cache.Headers.ContentLength ?? 0;
                LastModified = to_cache.Headers.LastModified;
                Expires = to_cache.Headers.Expires;
            }

            public void CopyTo(Response response) {
                response.Status = Result.Ok;
                response.Body = new MemoryStream(Content, false);
                response.Headers.ContentLength = ContentLength;
                response.Headers.ContentType = ContentType;
                response.Headers.LastModified = LastModified;
                response.Headers.Expires = Expires;
            }
        }
    }
}
