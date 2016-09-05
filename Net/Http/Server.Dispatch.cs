using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;
    using Net.Tcp;
    using Script;

    public partial class Server {
        public Host FindHost(string Query) {
            var Len = Hosts.Count;
            var Elems = Hosts.Elements;

            for (int i = 0; i < Len; i++) {
                var Host = Elems[i];
                if (Host.Matcher.Match(Query) != null)
                    return Host;
            }
            return null;
        }

        private async Task ClientConnected(Client client) {
			try {
                client.ReceiveTimeout = 5000;

				while (client.Connected) {
                    var Recv = Net.Http.Packet.Receive(client);
                    var Packet = await Recv;
					if (Packet == null) goto closeConnection;

					var Host = FindHost(Packet.Host);
					var Request = new Request(client, Packet, Host);
					if (Host == null) goto badRequest;

					OnClientRequest(Request);

                    if (Packet.Connection == "close") goto closeConnection;
                    else continue;

				badRequest:
					Request.Result = Result.BadRequest;
					Request.Finish();

					await Task.Delay(10);
				}
			}
			catch (Exception Error) {
                App.Log.Error(Error.ToString());
            }

        closeConnection:
            client.Close();
        }
        
        private static void OnClientRequest(Request Request) {
            var Target = Request.Packet.Target;
            if (!Request.Host.Handlers.MatchAndInvoke(Target, Request)) {
                var WWW = Request.Host.GetFullPath(Target);
                var EXT = Request.Host.GetExtension(WWW);

                Request.Set("FileName", WWW);
                Request.Set("FileEXT", EXT);

                if (!Request.Host.Handlers.MatchAndInvoke(EXT, Request))
                    HandleFile(Request, WWW);
            }

            Request.Finish();
        }

        private static void HandleFile(Request Request, string WWW) {
            Cache.Item Cached = null;

            if (Request.Host.Cache.TryGetValue(WWW, out Cached)) {
				if (Cached.LastWriteTime == Request.Packet.IfModifiedSince) {
					Request.Result.Status = Result.NotModified;
				}
				else {
                    if (Cached.IsCompressed) {
                        if (Request.CompressionEnabled) {
                            Request.Result.Headers["Vary"] = "Accept-Encoding";
                            Request.Result.Headers["Content-Encoding"] = "gzip";
                            Request.Data = Cached.Content;
                        }
                        else {
                            Request.Data = Cached.Info.OpenRead();
                        }
                    }
                    else {
                        Request.Data = Cached.Content;
                    }

                    Request.Result.Headers["Last-Modified"] = Cached.LastWriteTime;
                    Request.Result.ContentType = Cached.ContentType;
                }
            }
            else {
                Request.Result = Result.NotFound;
            }
        }
    }
}
