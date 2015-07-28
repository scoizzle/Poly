using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public class User : jsComplex {
        public static string TemplateFormat = "{Nick::Escape}!{Ident}@{Host}";

        public string Nick,
                      Ident,
                      Host,
                      Realname,
                      Password;

        public jsObject Modes;

        public User() {
            Nick = Ident = Host = Realname = Password = string.Empty;
            Modes = new jsObject();
        }

        public User(string Raw) : this() {
            if (Raw.Match(TemplateFormat, false, this) == null)
                Nick = Raw;
        }

        public User(jsObject Obj) {
            Obj.CopyTo(this);
        }

        public override string ToString() {
            return this.Template(TemplateFormat);
        }
    }
}