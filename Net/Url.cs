using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;
using Poly.Script;

namespace Poly.Net {
    public class Url : jsObject {
        public string Protocol {
            get {
                return getString("Protocol");
            }
            set {
                Set("Protocol", value);
            }
        }

        public string Host {
            get {
                return getString("Host");
            }
            set {
                Set("Host", value);
            }
        }

        public string Path {
            get {
                return getString("Path");
            }
            set {
                Set("Path", value);
            }
        }

        public jsObject Query {
            get {
                return getObject("Query");
            }
            set {
                Set("Query", value);
            }
        }

        public Url(string Url) {
            Parse(Url);
        }

        public Url(jsObject Obj) {
            Obj.CopyTo(this);
        }

        public new bool Parse(string Url) {
            var Matches = Url.Match("{Protocol}://{Routing}/{Path}");

            if (Matches == null)
                return false;

            this.Set("Protocol", Matches.getString("Protocol"));

            var Route = Matches.getString("Routing");
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

            Route = Matches.getString("Path");

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
        }

        public override string ToString(bool HumanFormat = false) {
            StringBuilder Output = new StringBuilder();

            Output.Append(Protocol);
            Output.Append("://");

            if (ContainsKey("Username")) {
                Output.Append(
                    getString("Username")
                );

                if (ContainsKey("Password")) {
                    Output.Append(":");
                    Output.Append(
                        getString("Password")
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
                Output.Append('#');
                Output.Append(
                    getString("Fragment")
                );
            }

            return Output.ToString();
        }
    }
}
