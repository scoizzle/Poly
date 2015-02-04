using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IO = System.IO;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    public class Host : jsComplex {
        public bool SessionsEnabled;

        public string Name, Path, DefaultDocument, DefaultExtension, SessionCookieName, SessionDomain, SessionPath;

        public jsObject PathOverrides, Ports;

        public Event.Engine Handlers = new Event.Engine();

        public Host() {
            this.Name = "localhost";
            this.Path = "WWW/";
            this.DefaultDocument = "index.html";
            this.DefaultExtension = "htm";

            this.SessionsEnabled = true;
            this.SessionCookieName = "SessionId";
            this.SessionDomain = Name;
            this.SessionPath = "";

            this.PathOverrides = new jsObject();
            this.Ports = new jsObject();
        }

        public Host(jsObject Base) : this() {
            if (Base != null) {
                Base.CopyTo(this);
            }
        }

        public void On(string Path, Event.Handler Handler) {
            Handlers.Register(Path, Handler);
        }

        public void Load(Server Serv, string Name) {
            Serv.ScriptCache.Load(Path, Path + Name);
        }

        public string GetWWW(Request Request) {
            string Req = Request.Packet.Target;

            if (!Req.Contains(".") && !Req.EndsWith("/")) {
                Req += "/";
            }

            string FileName = Req;

            foreach (var ovr in PathOverrides) {
                if (Req.Compare(ovr.Key)) {
                    FileName = ovr.Value.ToString();
                    break;
                }
            }
            try {
                FileName = IO.Path.GetFullPath(
                    Request.Host.Path + IO.Path.DirectorySeparatorChar + FileName
                );

                if (GetExtension(FileName) == string.Empty) {
                    FileName = IO.Path.GetFullPath(
                        FileName + IO.Path.DirectorySeparatorChar + DefaultDocument
                    );
                }

                return FileName;
            }
            catch { 
                return Req; 
            }
        }

        public string GetExtension(string FileName) {
            var Ext = IO.Path.GetExtension(FileName);

            if (string.IsNullOrEmpty(Ext)) {
                return "";
            }

            return Ext.Substring(1);
        }
    }
}
