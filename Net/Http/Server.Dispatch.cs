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
        public Host SearchForHost(string Query) {
            for (int i = 0; i < Hosts.Length; i++) {
                if (Hosts[i].Matcher.Match(Query) != null)
                    return Hosts[i];
            }
            return null;
        }

        new public async void OnClientConnect(Client Client) {
            while (Client.Connected) {
                Packet Packet = new Net.Http.Packet();
                Request Request = new Request(Client, Packet);

                if (!Packet.Receive(Client)) {
                    Client.Close();
                    break;
                }

                Request.Host = SearchForHost(Packet.Host);

                if (Request.Host == null) {
                    Request.Result = Result.BadRequest;
                    await Request.Finish();
                    continue;
                }
                else if (!Request.Host.Ports.IsEmpty) {
                    IPEndPoint Point = Client.Client.LocalEndPoint as IPEndPoint;

                    if (!Request.Host.Ports.ContainsValue(Point.Port)) {
                        Request.Result = Result.NoResponse;
                        await Request.Finish();
                        continue;
                    }
                }

                if (Request.Host.SessionsEnabled) {
                    Request.Session = Session.GetSession(this, Request, Request.Host);
                }

                try {
                    await OnClientRequest(Request);
                }
                catch {
                    Client.Close();
                    break;
                }

                Thread.Sleep(10);
            }
        }

        public async Task OnClientRequest(Request Request) {
			var WWW = Request.Host.GetWWW(Request.Packet.Target);
            var EXT = Request.Host.GetExtension(WWW);

            var Args = new jsObject(
                "Server", this,
                "Request", Request,
                "FileName", WWW,
                "FileEXT", EXT
            );

            if (Request.Host.Handlers.MatchAndInvoke(Request.Packet.Target, Args, true) ||
                Handlers.MatchAndInvoke(Request.Packet.Target, Args, true) ||
                Handlers.MatchAndInvoke(EXT, Args)) { }
            else {
                HandleFile(Request, WWW);
            }

            await Request.Finish();    
        }

        public virtual void HandleFile(Request Request, string WWW) {
            Cache.Item Cached;

            if (Request.Host.Cache.TryGetValue(WWW, out Cached)) {
                if (Cached.LastWriteTime == Request.Packet.Headers.Get<string>("If-Modified-Since")) {
                    Request.Result = Result.NotModified;
                }
                else {
                    Request.Result.ContentType = Cached.ContentType;
                    Request.Result.Headers["Last-Modified"] = Cached.LastWriteTime;

                    if (Cached.Content == null) {
                        Request.Data = File.OpenRead(WWW);
                    }
                    else {
                        Request.Data = new MemoryStream(Cached.Content, false);
                    }
                }
            }
            else {
                Request.Result = Result.NotFound;
            }
        }
    }
}
