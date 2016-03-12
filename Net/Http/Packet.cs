using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    public class Packet : jsComplex {
        static readonly char[] PathSplit = new char[] { '/' };
        public int ContentLength;
        public new jsObject Get;
        public jsObject Headers, Post, Cookies, Route;
        public string Host, RawTarget, Connection, Type, Target, Version, Value, Query, ContentType;

        public Packet() {
            Headers = new jsObject();
            Get = new jsObject();
            Post = new jsObject();
            Cookies = new jsObject();
            Route = new jsObject();

            ContentLength = 0;
            Host = RawTarget = Connection = Type = Target = Version = Value = Query = ContentType = string.Empty;
        }

        public bool Receive(Net.Tcp.Client Client) {
            if (!Client.Connected)
                return false;
            
            var Line = Client.ReadLine();

            if (string.IsNullOrEmpty(Line))
                return false;

            var Split = Line.Split(' ');

            if (Split.Length != 3)
                return false;

            Type = Split[0];
            RawTarget = Split[1];
            Version = Split[2];

            if (RawTarget.Contains("?")) {
                var QueryStartIndex = RawTarget.IndexOf('?');
                Query = RawTarget.Substring(QueryStartIndex + 1);
                Target = Uri.UnescapeDataString(RawTarget.Substring(0, QueryStartIndex));

                Split = Query.Split('&');

                for (int n = 0; n < Split.Length; n++) {
                    var Pair = Split[n].Split('=');

                    if (Pair.Length != 2) {
                        return false;
                    }

                    Get[Pair[0]] = Pair[1];
                }
            }
            else {
                Target = Uri.UnescapeDataString(Split[1]);
            }


            foreach(var Part in Target.Split(PathSplit, StringSplitOptions.RemoveEmptyEntries)){
                Route.Add(Part);
            }

            while (!string.IsNullOrEmpty(Line = Client.ReadLine())) {
                var Index = Line.Find(": ");

                if (Index == -1) {
                    Client.Close();
                    return false;
                }

                Headers.Set(
                    Line.Substring(0, Index),
                    Line.Substring(Index + 2)
                );
            }

            if (!Headers.ContainsKey("Host"))
                return false;

            Host = Headers["Host"] as string;

            if (Host.Contains(":")) {
                Headers.Set("Port", Host.Substring(":", ""));
                Host = Host.Substring("", ":");
            }

            if (Headers.ContainsKey("Cookie")) {
                var RawCookie = Headers.Get<string>("Cookie");
                Split = RawCookie.Split(';');

                for (int n = 0; n < Split.Length; n++) {
                    var Pair = Split[n].Split('=');
                    Cookies[Pair[0]] = Pair[1];
                }
            }

            if (Headers.ContainsKey("Content-Type"))
                ContentType = Headers.Get<string>("Content-Type");
            
            if (Headers.ContainsKey("Content-Length")) {
                ContentLength = Headers.Get<int>("Content-Length");

                if (ContentLength > 0) {
                    var RawContent = new char[ContentLength];
                    Client.Reader.ReadBlock(RawContent, 0, ContentLength);

                    Value = new string(RawContent);
                    RawContent = null;

                    if (Type == "POST" && ContentType == "application/x-www-form-urlencoded") {
                        Split = Value.Split('&');

                        for (int n = 0; n < Split.Length; n++) {
                            var Pair = Split[n].Split('=');

                            Post.Set(Pair[0], Pair[1]);
                        }
                    }
                }
            }

            return true;
        }
    }
}