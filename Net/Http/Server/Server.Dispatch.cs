using System;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;
    using Tcp;

    public partial class Server {
        async Task OnClientConnected(Client client) {
            Request     request = new Request(client);
            Response    response = new Response(client);
            JSON        arguments = new JSON();

            do {
                if (!await request.Receive())
                    return;

                Semaphore.Wait();

                var hostname_found =
                    request.Headers.TryGetValue(
                        "Host",
                        out string hostname);

                if (hostname_found && Host.Matcher.Compare(hostname)) {
                    try {
                        var handler_found =
                            RequestHandlers.TryGetHandler(
                                request.Target,
                                arguments,
                                out Handler handler);

                        if (handler_found) {
                            handler(request, response, arguments);
                        }
                        else {
                            if (UseStaticFiles) {
                                var file_name = Host.GetDocumentName(request.Target);

                                var cached_found =
                                    Cache.TryGetValue(
                                        file_name,
                                        out Cache.Item cached);

                                if (cached_found) {
                                    if (cached.LastWriteTimeUtc.Compare(request.LastModified)) {
                                        response.Status = Result.NotModified;
                                    }
                                    else {
                                        response.Content = Cache.GetStream(cached);
                                        response.ContentType = cached.ContentType;
                                        response.ContentLength = cached.ContentLength;
                                        response.LastModified = cached.LastWriteTimeUtc;
                                    }
                                }
                                else {
                                    response.Status = Result.NotFound;
                                }
                            }
                            else {
                                response.Status = Result.NotFound;
                            }
                        }
                    }
                    catch (IOException) {
                        break;
                    }
                    catch (Exception Error) {
                        response.Reset();
                        response.Status = Result.InternalError;

                        Log.Debug(Error.ToString());
                    }
                }
                else {
                    response.Status = Result.BadRequest;
                }
                
                Semaphore.Release();

                if (!await Finish(request, response))
                    break;

                request.Reset();
                response.Reset();
                arguments.Clear();
            }
            while (Running && await client.IsConnected());
        }

        async Task<bool> Finish(Request request, Response response) {
            response.Version = request.Version;
            response.Date = DateTime.UtcNow.HttpTimeString();

            bool send_body =
                !request.HeadersOnly &&
                response.Body.HasContent;

            if (send_body) {
                response.Content.Position = 0;
                response.ContentLength = response.Content.Length;
            }
            else {
                response.ContentLength = 0;
            }

            var send_headers = response.Send();

            if (!await send_headers)
                return false;

            if (send_body) {
                var send_content = response.Body.Send();

                return await send_content;
            }

            return true;
        }
    }
}
