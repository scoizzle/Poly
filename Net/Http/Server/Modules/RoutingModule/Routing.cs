using System;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Collections;

    public partial class RoutingModule : HttpServer.Module {
        MatchingCollection<HttpServer.RequestHandler> route_handlers;

        internal RoutingModule(HttpServer http_server) {
            route_handlers = new MatchingCollection<HttpServer.RequestHandler>('/');
        }

        public void Add(string path, HttpServer.RequestHandler handler) =>
            route_handlers.Add(path, handler);

        public void Remove(string path, HttpServer.RequestHandler handler) =>
            route_handlers.Remove(path);

        public HttpServer.RequestHandler Build(HttpServer.RequestHandler next) =>
            context => {
                var handler = route_handlers.Get(
                    context.Request.Path, 
                    (k, v) => {
                        context.Items[k] = v;
                        return true;
                    });

                return (handler ?? next)(context);
            };
    }

    public static class RoutingModuleExtensions {
        public static HttpServer UseRouting(this HttpServer server) {
            server.Modules.Add(new RoutingModule(server));
            return server;
        }

        public static RoutingModule GetRoutingModule(this HttpServer server) {
            var routing = server.Modules.Get<RoutingModule>();

            if (routing == null) {
                routing = new RoutingModule(server);
                server.Modules.Add(routing);
            }

            return routing;
        }

        public static void Route(this HttpServer server, string path, HttpServer.RequestHandler handler) =>
            GetRoutingModule(server).Add(path, handler);
            
            
        public delegate string RequestStringHandler(HttpServer.Context context);
        public static void Route(this HttpServer server, string path, RequestStringHandler handler) =>
            GetRoutingModule(server).Add(path, _ => {
                var output = handler(_);

                if (string.IsNullOrEmpty(output)) {
                    _.Response.Status = Result.NoContent;
                }
                else {
                    var stream = output.GetStream();

                    _.Response.Status = Result.Ok;

                    _.Response.Body = stream;
                    _.Response.Headers.ContentLength = stream.Length;
                }

                return Task.CompletedTask;
            });
    }
}
