using System.Collections.Generic;
using System.IO;

namespace Poly.Net.Http {
    using Collections;

    public partial class StaticFileModule : HttpServer.Module {
        private HttpServer server;
        private HttpServer.Host host;
        private DirectoryInfo document_path;
        private Dictionary<string, HttpServer.RequestHandler> files;

        internal StaticFileModule(HttpServer http_server) {
            server = http_server;
            host = server.Config.Host;
            document_path = host.DocumentPath;

            files = new Dictionary<string, HttpServer.RequestHandler>();
            Load(document_path);
        }

        public HttpServer.RequestHandler Build(HttpServer.RequestHandler next) =>
            context => files.TryGetValue(context.Request.Path, out HttpServer.RequestHandler handler) ? 
                handler(context) : next(context);
    }

    public static class StaticFilesExtensions {
        public static HttpServer UseStaticFiles(this HttpServer server) {
            server.Modules.Add(new StaticFileModule(server));
            return server;
        }
    }
}