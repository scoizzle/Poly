using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Http {
    using Data;
    using Net.Tcp;
    using Script;

    public partial class Server {
        public static void StaticFileHandler(string FileName, Request Request) {
            Request.Result.Data = File.ReadAllBytes(FileName);

            Request.Result.MIME = Server.GetMime(
                Request.Host.GetExtension(FileName)
            );
        }

        public virtual void DefaultFileHandler(string FileName, Request Request) {
            CachedFile Cache = null;

            if (FileCache.IsCurrent(FileName)) {
                Cache = FileCache[FileName];
            }
            else {
                Cache = new CachedFile(FileName) {
                    MIME = GetMime(Request.Host.GetExtension(FileName))
                };

                if (Cache.Data.Length < 15851760 && FileCache != null) {
                    FileCache[FileName] = Cache;
                }
            }

            Request.Result.MIME = Cache.MIME;
            Request.Result.Data = Cache.Data;
            Request.Result.Headers["Last-Modified"] = Cache.LastWriteTime.HttpTimeString();
        }

        public virtual void OnClientRequest(Request Request) {
            var Args = new jsObject(
                "Server", this,
                "Request", Request,
                "FileName", Request.Packet.Target
            );

            if (Request.Host.Handlers.MatchAndInvoke(Request.Packet.Target, Args, true)) { }                
            else if (Handlers.MatchAndInvoke(Request.Packet.Target, Args, true)) { }
            else {
                var WWW = Request.Host.GetWWW(Request);

                Args["FileName"] = WWW;

                var Ext = Request.Host.GetExtension(WWW);

                var MIME = GetMime(
                    Ext
                );

                if (!Handlers.MatchAndInvoke(MIME, Args)) { 
                    if (Request.Packet.Headers.Get<string>("If-Modified-Since") == File.GetLastWriteTimeUtc(WWW).HttpTimeString()) {
                        Request.Result = Result.NotModified;
                    }
                    else if (File.Exists(WWW)) {
                        DefaultFileHandler(WWW, Request);
                    }
                    else {
                        Request.Result = Result.NotFound;
                    }
                }
            }
            Request.Finish();
        }

        public override void OnClientConnect(Client Client) {
            Client.AutoFlush = true;
            Client.Socket.ReceiveTimeout = 15000;

            while (Client.Connected) {
                Packet Packet = new Net.Http.Packet();
                Request Request = new Request(Client, Packet);

                if (!Packet.Receive(Client)) {
                    Client.Close();
                    break;
                }

                Request.Host = Hosts.Search<Host>(Packet.Host, true, true);

                if (Request.Host == null) {
                    Request.Result = Result.BadRequest;
                    Request.Finish();
                    continue;
                }
                else if (!Request.Host.Ports.IsEmpty) {
                    IPEndPoint Point = Client.Socket.LocalEndPoint as IPEndPoint;

                    if (!Request.Host.Ports.ContainsValue(Point.Port)) {
                        Request.Result = Result.BadRequest;
                        Request.Finish();
                        continue;
                    }
                }

                if (Request.Host.SessionsEnabled) {
                    Request.Session = Session.GetSession(this, Request, Request.Host);
                }

                try {
                    OnClientRequest(Request);
                }
                catch {
                    Client.Close();
                    break;
                }
            }
        }
    }
}
