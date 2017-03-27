using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public class User : JSON {
        public static Matcher Fmt = new Matcher("{Nick:![!]}(!{Ident}@{Host})?");

		public string Nick { 
            get { return Get<string>("Nick"); }
            set { Set("Nick", value); }
        }		

		public string Ident { 
            get { return Get<string>("Ident"); }
            set { Set("Ident", value); }
        }

		public string Host { 
            get { return Get<string>("Host"); }
            set { Set("Host", value); }
        }		

		public string Realname { 
            get { return Get<string>("Realname"); }
            set { Set("Realname", value); }
        }
        
        public JSON Modes { 
            get { return Get<JSON>("Modes"); }
            set { Set("Modes", value); }
        }

        public User() {
            Nick = Ident = Host = Realname = string.Empty;
            Modes = new JSON();
        }

        public User(string Raw) : this() {
            Fmt.Match(Raw, this);
        }

        public User(JSON Obj) {
            Obj.CopyTo(this);
        }

        public override string ToString() {
            return Fmt.Template(this);
        }
    }
}