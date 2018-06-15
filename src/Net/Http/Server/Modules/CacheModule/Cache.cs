using System;
using System.Collections.Generic;

namespace Poly.Net.Http {
    using Collections;

    public partial class CacheModule : HttpServer.Module {
        private Dictionary<string, Item> cached_responses;

        internal CacheModule(HttpServer http_server) {
            cached_responses = new Dictionary<string, Item>();
        }

        public HttpServer.RequestHandler Build(HttpServer.RequestHandler next) =>
            async context => {
                var path = context.Request.Path;
                var is_cached = cached_responses.TryGetValue(path, out Item cached);

                if (!is_cached || DateTime.UtcNow > cached.Expires) {
                    await next(context);
                    
                    if (context.Response.Headers.Expires == default || context.Response.Headers.ContentLength == 0)
                        return;
                    
                    cached = new Item(context.Response);
                    cached_responses[path] = cached;
                }

                if (context.Request.Headers.IfModifiedSince == cached.LastModified) {
                    context.Response.Status = Status.NotModified;
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