using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public class User : jsObject {
        public static string TemplateFormat = "{Nick}!{Ident}@{Host}";

        public User() { }

        public User(string Raw) {
            if (!Base.Extract(TemplateFormat, Raw))
                Nick = Raw;
        }

        public string Nick {
            get {
                return Get<string>("Nick", string.Empty);
            }
            set {
                Set("Nick", value);
            }
        }

        public string Ident {
            get {
                return Get<string>("Ident", string.Empty);
            }
            set {
                Set("Ident", value);
            }
        }

        public string Host {
            get {
                return Get<string>("Host", string.Empty);
            }
            set {
                Set("Host", value);
            }
        }
        
        public string RealName {
            get {
                return Get<string>("RealName", string.Empty);
            }
            set {
                Set("RealName", value);
            }
        }

        public string Password {
            get {
                return Get<string>("Password", string.Empty);
            }
            set {
                Set("Password", value);
            }
        }

        public jsObject Modes {
            get {
                if (!ContainsKey("Modes")){ 
                    Modes = new jsObject(){ IsArray = true };
                }
                return Get<jsObject>("Modes", jsObject.NewArray);
            }
            set {
                Set("Modes", value);
            }
        }

        public override string ToString() {
            return Base.Template(TemplateFormat);
        }
    }
}