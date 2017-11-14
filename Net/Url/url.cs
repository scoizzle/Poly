using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net {
    public class Url {
		static Matcher UrlMatcher = new Matcher("{Scheme}://({Username}(:{Password})?@)?{Hostname}(:{Port as Int32})?(/{Path})?(\\?{Query})?(#{Fragment})?");
        static Serializer Serializer = Serializer.GetCached<Url>();

        public string   Scheme;
        public string   Username;
        public string   Password;
        public string   Hostname;
        public int      Port;
        public string   Path;
        public string   Query;
        public string   Fragment;
        
        public override string ToString() {
            return UrlMatcher.Template<Url>(this, out string result) ? result
                                                                     : null;
        }

        public static Url Parse(string text) {
            return UrlMatcher.Extract<Url>(text, out Url storage) ? storage
                                                                  : null;
        }
    }
}