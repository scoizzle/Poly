using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net {
    public class Url : JSON {
		static Matcher UrlMatcher = new Matcher("{Protocol}://({Username:![\\:@]}(:{Password:![@]})?\\@)?{Host:![\\:]}(:{Port:Numeric})?/{Path:![?#]}(?{Query:![#]})?(#{Fragment})?");

        public Url(string Url) {
            Parse(Url);
        }

        public new bool Parse(string Url) {
			return UrlMatcher.Match(Url, this) != null;
        }

        public override string ToString() {
            return ToString(false);
        }
    }
}
