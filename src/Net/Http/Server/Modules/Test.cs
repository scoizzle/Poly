using System;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    public class HttpTestModule : HttpServer.Module {
        public byte[] ModuleSpecificData = "1234567".GetBytes();

        public HttpServer.RequestHandler Build(HttpServer.RequestHandler next) {
            return context => {
                var stream = ModuleSpecificData.GetStream();

                context.Response.Status = Status.Ok;
                context.Response.Body = stream;
                context.Response.Headers.ContentLength = stream.Length;

                return Task.CompletedTask;
            };
        }
    }

    public static class HttpTestModuleExtensions {
        public static HttpServer UseHttpTestModule(this HttpServer server) {
            server.Modules.Add(new HttpTestModule());
            return server;
        }
    }
}
