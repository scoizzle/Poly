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
            Request.Print(File.ReadAllBytes(FileName));

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
                var Info = new FileInfo(FileName);

                if (Info.Length < 15851760) {
                    Cache = new CachedFile(FileName) {
                        MIME = GetMime(Request.Host.GetExtension(FileName))
                    };

                    if (FileCache != null) {
                        FileCache[FileName] = Cache;
                    }
                }
                else {
                    Request.Result.MIME = GetMime(Info.Extension.Substring(1));
                    Request.Result.Headers["Last-Modified"] = Info.LastWriteTime.HttpTimeString();
                    Request.Result.Headers["Content-Length"] = Info.Length.ToString();
                    Request.SendReply();
                    
                    using (var Reader = File.Open(FileName, FileMode.Open)) {
                        Reader.CopyToAsync(Request.Client.GetStream());
                    }

                    Request.Handled = true;
                    return;
                }
            }

            Request.Result.MIME = Cache.MIME;
            Request.Result.Headers["Last-Modified"] = Cache.LastWriteTime.HttpTimeString();
            Request.Print(Cache.Data);
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
                string WWW, Ext, MIME;
                Args["FileName"] = WWW = Request.Host.GetWWW(Request);
                Args["FileExtension"] = Ext = Request.Host.GetExtension(WWW);
                Args["FileMIME"] = MIME = GetMime(Ext);

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
            Client.Socket.ReceiveTimeout = 10000;

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
