using System;
using System.IO;

namespace Poly.Net.Http {
    public partial class CacheModule : HttpServer.Module {
        class Item {
            public byte[] Content;
            public long ContentLength;
            public string ContentType;
            public DateTime LastModified;
            public DateTime Expires;

            public Item(Response to_cache) {
                ContentType = to_cache.Headers.ContentType;
                ContentLength = to_cache.Headers.ContentLength;
                LastModified = to_cache.Headers.LastModified.Value;
                Expires = to_cache.Headers.Expires.Value;
                
                Content = new byte[ContentLength];
                to_cache.Body.Read(Content, 0, Content.Length);
            }

            public void CopyTo(Response response) {
                response.Status = Status.Ok;
                response.Body = Content.GetStream();
                response.Headers.ContentLength = ContentLength;
                response.Headers.ContentType = ContentType;
                response.Headers.LastModified = LastModified;
                response.Headers.Expires = Expires;
            }
        }
    }
}
