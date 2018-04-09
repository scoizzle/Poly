using System;
using System.IO;
using System.Threading.Tasks;


namespace Poly.Net.Http {
    public partial class StaticFileModule : HttpServer.Module {
        private Stream OpenFile(FileInfo info) =>
            File.Open(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);

        private HttpServer.RequestHandler GetHandler(FileInfo file) {
            var mime = GetMIME(file.Extension);
            var length = file.Length;
            var date = file.LastWriteTimeUtc.ToHttpTime();

            return context => {
                var response = context.Response;
                var headers = response.Headers;
                
                response.Status = Result.Ok;
                response.Body = OpenFile(file);

                headers.ContentLength = length;
                headers.ContentType = mime;
                headers.LastModified = date;
                headers.Expires = DateTime.UtcNow.AddSeconds(server.Config.CacheResourceMaxAge);

                return Task.CompletedTask;
            };
        }
    }
}
