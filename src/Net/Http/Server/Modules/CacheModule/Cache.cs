using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Collections;

    public partial class CacheModule : HttpServer.Module {
        Dictionary<string, Item> cached_responses;

        internal CacheModule(HttpServer http_server) {
            cached_responses = new Dictionary<string, Item>();

            cleanup_expired_responses();
        }

        private void cleanup_expired_responses() {
            var now = DateTime.UtcNow;
            var expired = cached_responses.Where(_ => now > _.Value.Expires).ToArray();

            foreach (var pair in expired)
                cached_responses.Remove(pair.Key);

            Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => cleanup_expired_responses());
        }

        private bool ShouldCache(Response response) =>
            response.Headers.Expires != default &&
            response.Headers.ContentLength != 0;

        public HttpServer.RequestHandler Build(HttpServer.RequestHandler next) =>
            async context => {
                var path = context.Request.Path;

                if (cached_responses.TryGetValue(path, out Item cached)) {
                    if (context.Request.Headers.IfModifiedSince == cached.LastModified) {
                        context.Response.Status = Status.NotModified;
                        context.Response.Headers.ContentLength = 0;
                    }
                    else {
                        cached.CopyTo(context.Response);
                    }
                }
                else {
                    await next(context);

                    if (!ShouldCache(context.Response)) 
                        return;

                    cached = new Item(context.Response);
                    cached_responses[path] = cached;
                }
            };
    }

    public static class CacheModuleExtensions {
        public static HttpServer UseCache(this HttpServer server) {
            server.Modules.Add(new CacheModule(server));
            return server;
        }
    }
}