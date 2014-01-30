using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Http {
    using Data;
    using Event;
    using Net.Tcp;
    using Script;

    public partial class Server {
        public virtual void SendReply(Client Client, Request Request) {
            if (!Request.Handled)
                Request.Finish();

            if (Request.Packet.Connection == "keep-alive") {
                Request.Result.Headers["Connection"] = "Keep-Alive";
                Request.Result.Headers["Keep-Alive"] = "timeout=15, max=99";
            }

            SendReply(Client, Request.Result);
        }

        public virtual void SendReply(Client Client, Result Result) {
            if (!Client.Connected)
                return;

            Client.Send(Result.BuildReply());
            Client.Send(Result.Data);
        }

        public void StaticFileHandler(string FileName, Request Request) {
            Request.Result.Data = File.ReadAllBytes(FileName);

            Request.Result.MIME = GetMime(
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
            var WWW = Request.Host.GetWWW(Request);
            var Ext = Request.Host.GetExtension(WWW);

            var MIME = GetMime(
                Ext
            );

            var Args = new jsObject(
                "Server", this,
                "Request", Request,
                "FileName", WWW
            );

            if (Handlers.MatchAndInvoke(Request.Packet.Target, Args, true)) {
                return;
            }

            if (!File.Exists(WWW)) {
                Request.Result = Result.NotFound;
            }
            else if (Request.Packet.Headers.getString("If-Modified-Since") == File.GetLastWriteTimeUtc(WWW).HttpTimeString()) {
                Request.Result = Result.NotModified;
            }
            else if (Handlers.MatchAndInvoke(MIME, Args)) {
            }
            else{
                DefaultFileHandler(WWW, Request);
            }
        }

        public override void OnClientConnect(Client Client) {
            Client.autoFlush = true;
            Client.ReceiveTimeout = 15000;

            while (Client.Connected) {
                Packet Packet = new Net.Http.Packet();

                try {
                    if (!Packet.Receive(Client)) {
                        SendReply(Client, Result.InternalError);
                        return;
                    }
                }
                catch (Exception Error) {
                    App.Log.Error(Error.ToString());
                }

                Host Host = Hosts.Search<Host>(Packet.Host);

                if (Host == null) {
                    try {
                        SendReply(Client, Result.InternalError);
                        continue;
                    }
                    catch { }
                }

                Request Request = new Request(Client, Packet) {
                    Host = Host
                };

                try {
                    OnClientRequest(Request);

                    SendReply(Client, Request);
                }
                catch (Exception Error) {
                    App.Log.Error(Error.ToString());
                    Client.Close();
                }
            }
        }
    }
}
