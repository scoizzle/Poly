using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public class User {
        public static Matcher Fmt = new Matcher("{Nick:![!]}(!{Ident}@{Host})?");

		JSON js;

		public string Nick { 
            get { return js.Get<string>("Nick"); }
			set { js.Set("Nick", value); }
        }		

		public string Ident { 
            get { return js.Get<string>("Ident"); }
			set { js.Set("Ident", value); }
        }

		public string Host { 
            get { return js.Get<string>("Host"); }
			set { js.Set("Host", value); }
        }		

		public string Realname { 
            get { return js.Get<string>("Realname"); }
			set { js.Set("Realname", value); }
        }
        
        public JSON Modes { 
            get { return js.Get<JSON>("Modes"); }
			set { js.Set("Modes", value); }
        }

        public User() {
            Nick = Ident = Host = Realname = string.Empty;
            Modes = new JSON();
			js = new JSON();
        }

        public User(string Raw) : this() {
			Fmt.Match(Raw, js);
        }

		public User(JSON json) {
			js = json;
		}

        public override string ToString() {
            return Fmt.Template(js);
        }

		public static implicit operator JSON(User user) {
			return user.js;
		}

		public static implicit operator User(JSON json) {
			return new User(json);
		}
    }
}