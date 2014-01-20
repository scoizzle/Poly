using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IO = System.IO;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    public class Host : jsObject {
        public Host() {
        }

        public Host(jsObject Base) {
            if (Base != null) {
                Base.CopyTo(this);
            }
        }

        public string Name {
            get {
                if (!this.ContainsKey("Name"))
                    return "localhost";
                return this.getString("Name");
            }
            set {
                this["Name"] = value;
            }
        }

        public string Path {
            get {
                if (!this.ContainsKey("Path"))
                    return "WWW/";
                return this.getString("Path");
            }
            set {
                this["Path"] = value;
            }
        }

        public string DefaultDocument {
            get {
                if (!this.ContainsKey("DefaultDocument"))
                    return "index.htm";
                return this.getString("DefaultDocument");
            }
            set {
                this["DefaultDocument"] = value;
            }
        }

        public string DefaultExtension {
            get {
                if (!this.ContainsKey("DefaultExtension"))
                    return "htm";
                return this.getString("DefaultExtension");
            }
            set {
                this["DefaultExtension"] = value;
            }
        }

        public jsObject PathOverrides {
            get {
                return Get<jsObject>("PathOverrides", () => { 
                    return new jsObject(); 
                });
            }
            set {
                Set("PathOverrides", value);
            }
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

        public string GetExtension(string FileName) {
            var Ext = IO.Path.GetExtension(FileName);

            if (string.IsNullOrEmpty(Ext)) {
                return "";
            }

            return Ext.Substring(1);
        }
    }
}
