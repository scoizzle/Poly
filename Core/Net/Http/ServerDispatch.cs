using System;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Tcp;

    public partial class Server {
        async Task OnClientConnected(Client client) {
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
					catch (Exception) {
						finalizer = Result.Send(client, Result.InternalError);
					}
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

		Task<bool> OnClientRequest(Request request) {
			var Target = request.Packet.Target;
            var EXT = Target.GetFileExtension();

            Request.Handler f;
            Cache.Item Cached;

			if (Handlers.TryGetHandler(EXT, out f))
                return f(request);

			if (Handlers.TryGetHandler(Target, request.Arguments, out f))
                return f(request);

            Target = request.Host.GetFullPath(Target);

            if ((Cached = Cache.Get(Target)) != null) {
                return request.SendFile(Cached);
            }

            return Result.Send(request.Client, Result.NotFound);
            
        }
    }
}
