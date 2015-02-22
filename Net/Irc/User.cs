using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public class User : jsObject {
        public static string TemplateFormat = "{Nick::Escape}!{Ident}@{Host}";

        public User() { }

        public User(string Raw) {
            if (!this.Extract(TemplateFormat, Raw))
                Nick = Raw;
        }

        public User(jsObject Obj) {
            Obj.CopyTo(this);
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
        
        public string Realname {
            get {
                return Get<string>("Realname", string.Empty);
            }
            set {
                Set("Realname", value);
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
                return Get<jsObject>("Modes", jsObject.NewObject);
            }
            set {
                Set("Modes", value);
            }
        }

        public override string ToString() {
            return this.Template(TemplateFormat);
        }
    }
}