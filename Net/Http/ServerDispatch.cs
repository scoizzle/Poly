using System;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Net.Tcp;

    public partial class Server {
        private async void OnClientConnected(Client client) {
            client.SendTimeout = ClientSendTimeout;
            client.ReceiveTimeout = ClientReceiveTimeout;

            var packet = new Packet();
            var request = new Request(client, packet, this);
            var finalizer = default(Task<bool>);
            
            do {
                if (await packet.Receive(client)) {
                    await sem.WaitAsync();

                    try {
                        if (Matcher.Compare(packet.Headers["Host"])) {
                            request.Prepare();
                            finalizer = OnClientRequest(request);
                        }
                        else {
                            finalizer = Result.Send(client, Result.BadRequest);
                        }
                    }
                    catch (IOException) { break; }
                    catch (Exception) { }
                    finally {
                        sem.Release();
                    }

                    await finalizer;
                    request.Reset();
                }
                else break;
            }
            while (Running && await client.IsConnected());
        }

        private Task<bool> OnClientRequest(Request request) {
            var Target = request.Packet.Request;
            var EXT = Target.GetFileExtension();

            Request.Handler f;
            Cache.Item Cached;

            if (EXT.Length != 0 &&
               (f = Handlers.GetHandler(EXT)) != null) {
                return f(request);
            }
            else
            if ((f = Handlers.GetHandler(Target, request.Arguments)) != null) {
                return f(request);
            }
            else {
                Target = request.Host.GetFullPath(Target);

                if ((Cached = Cache.Get(Target)) != null) {
                    return request.SendFile(Cached);
                }
                else {
                    return Result.Send(request.Client, Result.NotFound);
                }
            }
        }
    }
}
