using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net {
    public class Url : JSON {
		static Matcher UrlMatcher = new Matcher("{Protocol}://({Username:![\\:@]}(:{Password:![@]})?\\@)?{Host:![\\:]}(:{Port:Numeric})?/{Path:![?#]}(?{Query:![#]})?(#{Fragment})?");
        public string Protocol, Host, Path;
        public JSON Query;

        public Url(string Url) {
            Parse(Url);
        }

        public Url(JSON Obj) {
            Obj.CopyTo(this);
        }

        public new bool Parse(string Url) {
			return UrlMatcher.Match(Url, this) != null;
        }

        public override string ToString() {
            return ToString(false);
        }

        public override string ToString(bool HumanFormat = false) {
            return UrlMatcher.Template(this);
        }
    }
}
