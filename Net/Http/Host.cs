using System;
using System.Collections.Generic;
using Poly.Data;
using path = System.IO.Path;

namespace Poly.Net.Http {
    public class Host : jsComplex {
        public string Name, Path, DefaultDocument, DefaultExtension;

        public Matcher Matcher;

        public Event.Engine Handlers;
        public Cache Cache;

		public Host(string hostName) {
            this.Name = hostName;
            this.Path = "WWW";
            this.DefaultDocument = "/index.html";
            this.DefaultExtension = "htm";
            this.Handlers = new Event.Engine();
        }

        public Host(string hostName, jsObject args) : this(hostName) {
            args?.CopyTo(this);
        }

		public void Ready() {
            this.Matcher = new Matcher(this.Name);
            this.Path = path.GetFullPath (Path);
			this.Cache = new Cache(this.Path);
		}

        public void Stop() {
            this.Cache.Dispose();
        }

        public void On(string Path, Event.Handler Handler) {
            Handlers.Register(Path, Handler);
        }

        public void On(string Path, Event.Handler Handler, string ThisName, object This) {
            Handlers.Register(Path, Handler, ThisName, This);
        }
        
        public string GetWWW(string Target) {
            if (string.IsNullOrEmpty(Target) || Target == "/")
                return path.GetFullPath(Path + DefaultDocument);

            if (Target[Target.Length - 1] == '/') {
                return Path + Target + DefaultDocument;
            }
            
            return path.GetFullPath(Path + Target);
        }

        public string GetExtension(string FileName) {
            var lastPeriod = FileName.FindLast('.');

            if (lastPeriod == -1)
                return string.Empty;

            return FileName.Substring(lastPeriod + 1);
        }

        public object Psx(Server Serv, Request Request, string Target) {
            return Serv.Psx(Request, Target);
        }
    }
}
