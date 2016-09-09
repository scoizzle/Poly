using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    using Tcp;

    public class Packet : jsComplex {
        public static readonly Matcher HeaderMatcher = new Matcher("{Type} {RawTarget} {Version}\r\n{Headers -> `{Key}: {Value:![\r]}[\r\n]`}"),
                                       TargetMatcher = new Matcher("{Target:![?#]}[?{Query:![#]}][#{Fragment}]"),
                                       PathMatcher = new Matcher("/{Value:![/]}"),
                                       HostMatcher = new Matcher("{Hostname:![:]}[:{Port: Numeric -> Int}]"),
                                       QueryMembersMatcher = new Matcher("[&]{Key}={Value:!Whitespace,![&]}"),
                                       HeaderPropertiesMatcher = new Matcher("[; ]{Key}={Value:!Whitepsace}"),
                                       MultipartHeaderPropertiesMatcher = new Matcher("[;] {Key}=\"{Value}\""),
                                       MultipartBoundaryMatcher = new Matcher("{ContentType}; boundary={Boundary}");

        public static readonly byte[] DoubleNewLine = Encoding.Default.GetBytes("\r\n\r\n");

        public int Port;
        public long ContentLength;
        public new jsObject Get;
        public jsObject Post, Cookies, Route, Headers;
        public string Hostname, RawTarget, Connection, Type, Target, Version, Query, ContentType, AcceptEncoding, Boundary, IfModifiedSince;

        public Packet() {
            Get = new jsObject();
            Post = new jsObject();
            Cookies = new jsObject();
            Route = new jsObject();
            Headers = new jsObject();

            ContentLength = 0;
            Hostname = RawTarget = Connection = Type = Target = Version = Query = ContentType = Boundary = string.Empty;
        }

        public static async Task<bool> Forward(Client In, Client Out) {
            if (In.Connected && Out.Connected) {
                var Stream = In.GetStreamer();
                var Output = Out.GetStreamer();

                var Headers = new StringBuilder();
                long ContentLength = 0;

                try {
                    for (var c = 128; c != 0; c--) {
                        var Line = In.ReceiveLine();
                        if (Line == null) return false;

                        Headers.Append(Line).Append(App.NewLine);
                        if (Line.Length == 0) break;

                        if (ContentLength == 0)
                            if (string.Compare(Line, 0, "Content-Length: ", 0, 16, StringComparison.Ordinal) == 0)
                                long.TryParse(Line.Substring(16), out ContentLength);
                    }

                    Out.Send(Headers.ToString());

                    if (ContentLength > 0)
                        return await Stream.Receive(Output.Stream, ContentLength);
                }
                catch { return false; }
            }

            return true;
        }

        public static bool Receive(Client client, Packet recv) {
            string headers;

            try { headers = client.ReceiveStringUntil(DoubleNewLine, Encoding.Default); }
            catch { return false; }

            if (headers == null || headers.Length == 0) goto closeConnection;

            if (HeaderMatcher.Match(headers, recv) == null)
                return false;

            recv.Headers.ForEach<string>((key, value) => {
                ParseHeader(recv, key, value);
            });

            TargetMatcher.Match(recv.RawTarget, recv);

            QueryMembersMatcher.MatchAll(recv.Query, recv.Get, true);
			PathMatcher.MatchAll(recv.Target, recv.Route, true);

			if (recv.ContentLength > 0 && recv.Type == "POST") {
				switch (recv.ContentType) {
					case "application/x-www-form-urlencoded": 
						QueryMembersMatcher.MatchAll(client.ReceiveString(recv.ContentLength), recv.Post, true);
						break;

					case "multipart/form-data": {
						MultipartHandler Handler = new MultipartHandler(client, recv, client.Stream);

						if (!Handler.Receive())
							return false;
							
						break;
					}
				}
			}

            return true;

        closeConnection:
            client.Close();
            return false;
        }

        private static void ParseHeader(Packet recv, string Key, string Value) {
            switch (Key) {
                case "Host": 
                    HostMatcher.Match(Value, recv);
                    return;

				case "Content-Length":
					long.TryParse(Value, out recv.ContentLength);
					return;

				case "Content-Type":
                    MultipartBoundaryMatcher.Match(Value, recv);
					return;
					
				case "Cookie": 
					HeaderPropertiesMatcher.MatchAll(Value, recv.Cookies, true);
					return;

				case "Accept-Encoding":
					recv.AcceptEncoding = Value;
					return;

				case "If-Modified-Since":
					recv.IfModifiedSince = Value;
					return;
            }
        }
    }
}