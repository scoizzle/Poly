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

        public override string ToString() {
            return Name;
        }

        public override bool TryGet(string Key, out object Value) {
            switch (Key) {
                default:
                    return base.TryGet(Key, out Value);

                case "CacheSize": Value = CacheSize; break;
                case "Name": Value = Name; break;
                case "Path": Value = Path; break;
                case "DefaultDocument": Value = DefaultDocument; break;
                case "DefaultExtension": Value = DefaultExtension; break;
                case "Compressable": Value = Compressable; break;
            }
            return true;
        }

        public override void AssignValue(string Key, object Value) {
            switch (Key) {
                default: base.AssignValue(Key, Value); break;
                case "CacheSize": CacheSize = (long)(Value); break;
                case "Name": Name = Value?.ToString(); break;
                case "Path": Path = Value?.ToString(); break;
                case "DefaultDocument": DefaultDocument = Value?.ToString(); break;
                case "DefaultExtension": DefaultExtension = Value?.ToString(); break;
                case "Compressable": Compressable = Value as jsObject; break;
            }
        }
    }
}
