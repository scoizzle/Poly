using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;
using Poly.Script;

namespace Poly.Net {
    public class Url : jsComplex {
		static Matcher UrlMatcher = new Matcher("{Protocol}://[{Username:![\\:@]}[:{Password:![@]}]@]{Host:![\\:]}[:{Port:Numeric}]/{Path}[?{Query:![#]}][#{Fragment}]");
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
			/*
            var Matches = Url.Match("{Protocol}://{Routing}/{Path}");

            if (Matches == null)
                return false;

            this.Set("Protocol", Matches.Get<string>("Protocol"));

            var Route = Matches.Get<string>("Routing");
            var Host = Route;

            if (Route.Contains("@")) {
                var Index = Route.LastIndexOf('@');
                var Auth = Route.Substring(0, Index);

                if (Auth.Contains(':')) {
                    Index = Auth.IndexOf(':');

                    var Username = Auth.Substring(0, Index);
                    var Password = Auth.Substring(Index + 1);

                    this.Set("Username", Username);
                    this.Set("Password", Password);
                }
                else {
                    this.Set("Username", Auth);
                }

                Host = Route.Substring(Index + 1);
            }

            if (Host.Contains(':')) {
                var Index = Host.IndexOf(':');
                var Port = Host.Substring(Index + 1);

                Host = Host.Substring(0, Index);
                this.Set("Port", Port.ToInt());
            }

            this.Set("Host", Host);

            Route = Matches.Get<string>("Path");

            if (Route == null)
                Route = "";

            this.Set("Request", "/" + Route);

            if (Route.Contains('#')) {
                var Index = Route.IndexOf('#');

                this.Set("Fragment", Route.Substring(Index + 1));

                Route = Route.Substring(0, Index);
            }

            if (Route.Contains('?')) {
                var Index = Route.IndexOf('?');
                var Path = Route.Substring(0, Index);
                var Query = Route.Substring(Index + 1);

                this.Set("Path", Path);

                var Vars = Query.Split('&', ';');
                var QObj = new jsObject();

                for (Index = 0; Index < Vars.Length; Index++) {
                    var Pair = Vars[Index].Split('=');

                    if (Pair.Length != 2)
                        break;

                    var Key = Pair[0];
                    var Value = Pair[1];

                    QObj.Set(Key, Value);
                }

                this.Set("Query", QObj);
            }
            else {
                this.Set("Path", Route);
            }

            return true;
            */
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
