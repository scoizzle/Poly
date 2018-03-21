using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Collections;

    public partial class CacheModule : HttpServer.Module {
        private MatchingCollection<Item> cached_responses;

        internal CacheModule(HttpServer http_server) {
            cached_responses = new MatchingCollection<Item>('/');
        }

        public HttpServer.RequestHandler Build(HttpServer.RequestHandler next) =>
            async context => {
                var cached = cached_responses.Get(context.Request.Path);

                if (cached == null || DateTime.UtcNow > cached.Expires) {
                    await next(context);
                    
                    if (context.Response.Headers.Expires == default || context.Response.Headers.ContentLength == 0)
                        return;
                    
                    cached = new Item(context.Response);
                    cached_responses.Add(context.Request.Path, cached);
                }

                if (context.Request.Headers.IfModifiedSince == cached.LastModified) {
                    context.Response.Status = Result.NotModified;
                    context.Response.Headers.ContentLength = 0;
                    return;
                }
                
                cached.CopyTo(context.Response);
            };
    }

    public static class CacheModuleExtensions {
        public static HttpServer UseCache(this HttpServer server) {
            server.Modules.Add(new CacheModule(server));
            return server;
        }
    }
}