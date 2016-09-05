using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;
using Poly.Script;

namespace Poly.Net {
    public class Url : jsComplex {
		static Matcher UrlMatcher = new Matcher("{Protocol}://[{Username:![\\:@]}[:{Password:![@]}]\\@]{Host:![\\:]}[:{Port:Numeric}]/{Path}[?{Query:![#]}][#{Fragment}]");
        public string Protocol;
        public string Host;
        public string Path;
        public jsObject Query;

        public Url(string Url) {
            Parse(Url);
        }

        public Url(jsObject Obj) {
            Obj.CopyTo(this);
        }

        public new bool Parse(string Url) {
			return UrlMatcher.Match(Url, this) != null;
        }

        public override string ToString() {
            return ToString(false);
        }

        public override string ToString(bool HumanFormat = false) {
            StringBuilder Output = new StringBuilder();

            Output.Append(Protocol);
            Output.Append("://");

            if (ContainsKey("Username")) {
                Output.Append(
                    Get<string>("Username")
                );

                if (ContainsKey("Password")) {
                    Output.Append(":");
                    Output.Append(
                        Get<string>("Password")
                    );
                }

                Output.Append("@");
            }

            Output.Append(Host);
            Output.Append("/");
            Output.Append(Path);

            if (Query != null && Query.Count > 0) {
                Output.Append("?");

                Query.ForEach((Key, Val) => {
                    Output.Append(Key);
                    Output.Append("=");
                    Output.Append(Val);
                    Output.Append('&');
                });

                Output.Remove(Output.Length - 1, 1);
            }

            if (ContainsKey("Fragment")) {
                Output.Append('#')
                      .Append(Get<string>("Fragment"));
            }

            return Output.ToString();
        }
    }
}
