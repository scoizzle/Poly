using System;
using System.Collections.Generic;
using System.Linq;
using Poly.Data;
using path = System.IO.Path;

namespace Poly.Net.Http {
    public class Host : jsComplex {
        public long CacheSize;
        public string Name, Path, DefaultDocument, DefaultExtension;
		public jsObject Compressable;

        public Matcher Matcher;
         
        public Event.Engine Handlers;
        public Cache Cache;

        public Host(string hostName) {
            Name = hostName;
            Path = "WWW";
            DefaultDocument = "/index.html";
            DefaultExtension = "htm";
            Handlers = new Event.Engine();
            CacheSize = Cache.DefaultMaxSize;
            Compressable = new jsObject() { IsArray = true };
        }

        public Host(string hostName, jsObject args) : this(hostName) {
            args?.CopyTo(this);
        } 

        public void Ready() {
            Matcher = new Matcher(Name);
            Cache = new Cache(Path, Compressable.Values.Cast<string>().ToArray());
            Path = path.GetFullPath(Path);
        }

        public void Stop() {
            Cache.Dispose();
        }

        public void On(string Path, Event.Handler Handler) {
            Handlers.Register(Path, Handler);
        }

        public void OnPSX(string Path, Server Serv, string File) {
            Handlers.Register(Path, r => Serv.Psx(r as Request, File));
        }
        
        public string GetFullPath(string Target) {
            if (Target == null || Target.Length == 0 || Target == "/") 
				Target = DefaultDocument;
            else if (Target[Target.Length - 1] == '/') 
				Target = Target + DefaultDocument;
            return Target;
        }

        public string GetExtension(string FileName) {
            var lastPeriod = FileName.LastIndexOf('.');

            if (lastPeriod == -1)
                return string.Empty;

            return FileName.Substring(lastPeriod);
        }

        public override string ToString() {
            return Name;
        }
    }
}
