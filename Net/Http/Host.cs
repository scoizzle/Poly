using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IO = System.IO;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    public class Host : jsObject {
        public Event.Engine Handlers = new Event.Engine();

        public Host() {
        }

        public Host(jsObject Base) {
            if (Base != null) {
                Base.CopyTo(this);
            }
        }

        public string Name {
            get {
                return this.Get<string>("Name", "localhost");
            }
            set {
                this["Name"] = value;
            }
        }

        public string Path {
            get {
                return this.Get<string>("Path", "WWW/");
            }
            set {
                this["Path"] = value;
            }
        }

        public string DefaultDocument {
            get {
                return this.Get<string>("DefaultDocument", "index.html");
            }
            set {
                this["DefaultDocument"] = value;
            }
        }

        public string DefaultExtension {
            get {
                return this.Get<string>("DefaultExtension", "htm");
            }
            set {
                this["DefaultExtension"] = value;
            }
        }

        public string SessionCookieName {
            get {
                return this.Get<string>("SessionCookieName", "SessionId");
            }
            set {
                this["SessionCookieName"] = value;
            }
        }

        public bool SessionsEnabled {
            get {
                if (!this.ContainsKey("SessionsEnabled"))
                    return true;
                return this.Get<bool>("SessionsEnabled");
            }
            set {
                this.Set("SessionsEnabled", value);
            }
        }

        public jsObject PathOverrides {
            get {
                return Get<jsObject>("PathOverrides", jsObject.NewObject);
            }
            set {
                Set("PathOverrides", value);
            }
        }

        public jsObject Ports {
            get {
                return Get<jsObject>("Ports", jsObject.NewArray);
            }
            set {
                Set("Ports", value);
            }
        }

        public void On(string Path, Event.Handler Handler) {
            Handlers.Register(Path, Handler);
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
