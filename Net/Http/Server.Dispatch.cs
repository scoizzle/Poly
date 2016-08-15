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
            while (client.Connected) {
                var Packet = await Net.Http.Packet.Receive(client);
                if (Packet == null) goto closeConnection;

                var Host = FindHost(Packet.Host);
                var Request = new Request(client, Packet, Host);
                if (Host == null) goto badRequest;

                await OnClientRequest(Request);
                Thread.Sleep(5);
                continue;

            badRequest:
                Request.Result = Result.BadRequest;
                await Request.Finish();
                Thread.Sleep(10);
            }

        closeConnection:
            client.Close();
        }
        
        private async Task OnClientRequest(Request Request) {
            var Target = Request.Packet.Target;
            if (!Request.Host.Handlers.MatchAndInvoke(Target, Request)) {
                var WWW = Request.Host.GetWWW(Target);
                var EXT = Request.Host.GetExtension(WWW);

                Request.Set("FileName", WWW);
                Request.Set("FileEXT", EXT);

                if (!Request.Host.Handlers.MatchAndInvoke(EXT, Request))
                    HandleFile(Request, WWW);
            }

            await Request.Finish();
        }

        private static void HandleFile(Request Request, string WWW) {
            Cache.Item Cached;

            if (Request.Host.Cache != null && Request.Host.Cache.TryGetValue(WWW, out Cached)) {
                if (Cached.LastWriteTime == Request.Packet.Headers.Get<string>("If-Modified-Since")) {
                    Request.Result = Result.NotModified;
                }
                else {
                    Request.Result.Headers["Last-Modified"] = Cached.LastWriteTime;
                    Request.Result.ContentType = Cached.ContentType;
                    Request.Data = Cached.Content;
                }
            }
            else {
                Request.Result = Result.NotFound;
            }
        }
    }
}
