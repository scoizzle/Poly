using System;
using Poly.Data;
using Pth = System.IO.Path;

namespace Poly.Net.Http {
    public class Host : jsComplex {
        public bool SessionsEnabled;

        public string Name, Path, DefaultDocument, DefaultExtension, SessionCookieName, SessionDomain, SessionPath;

        public Matcher Matcher;
        public jsObject PathOverrides, Ports;

        public Event.Engine Handlers = new Event.Engine();
        public Cache Cache;

		public Host() {
            this.Name = "localhost";
            this.Path = "WWW";
            this.DefaultDocument = "index.html";
            this.DefaultExtension = "htm";

            this.SessionsEnabled = true;
            this.SessionCookieName = "SessionId";
            this.SessionDomain = Name;
            this.SessionPath = "";

            this.PathOverrides = new jsObject();
            this.Ports = new jsObject();
        }

		public void Ready() {
			this.Path = Pth.GetFullPath (Path);
			this.Cache = new Cache(this.Path);
		}

        public void On(string Path, Event.Handler Handler) {
            Handlers.Register(Path, Handler);
        }

        public void On(string Path, Event.Handler Handler, string ThisName, object This) {
            Handlers.Register(Path, Handler, ThisName, This);
        }
        
        public string GetWWW(string Target) {
			var WWW = Target[0] == '/' ?
				Path + Target :
				Path + '/' + Target;

			foreach (var ovr in PathOverrides) {
				if (WWW.Match(ovr.Key) != null) {
					WWW = ovr.Value as string;
				}
			}

			if (WWW.EndsWith ("/") || string.IsNullOrEmpty (Pth.GetExtension (WWW))) {
				WWW = WWW + DefaultDocument;
			}

			return WWW;
        }

        public string GetExtension(string FileName) {
            var Ext = Pth.GetExtension(FileName);

            if (string.IsNullOrEmpty(Ext)) {
                return "";
            }

            return Ext.Substring(1);
        }

        public object Psx(Server Serv, Request Request, string Target) {
			var File = Pth.GetFullPath(
                Path + Target
            );

            return Serv.Psx(Request, File);
        }
    }
}
