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
        public static readonly Matcher RequestHeaderMatcher = new Matcher("{Type} {RawTarget} HTTP/1.1*"),
                                       HeaderMatcher = new Matcher("\r\n{Key}: {Value:![\r]}"),
                                       PathMatcher = new Matcher("/{Value:![/]}"),
                                       HostMatcher = new Matcher("{Hostname:![:]}[:{Port: Numeric -> Int}]"),
                                       QueryMembersMatcher = new Matcher("[&]{Key}={Value:!Whitespace,![&]}"),
                                       HeaderPropertiesMatcher = new Matcher("[; ]{Key}={Value:!Whitepsace}"),
                                       MultipartHeaderPropertiesMatcher = new Matcher("[;] {Key}=\"{Value}\""),
                                       MultipartBoundaryMatcher = new Matcher("{ContentType}; boundary={Boundary}");

        public static readonly byte[] DoubleNewLine = Encoding.UTF8.GetBytes("\r\n\r\n");

        public int Port;
        public long ContentLength;
        public new jsObject Get;
        public jsObject Post, Cookies, Route, Headers;
        public string RawTarget,
                      Target,
                      Type,
                      Version,
                      Hostname,
                      Connection, 
                      Query,
                      ContentType,
                      AcceptEncoding,
                      IfModifiedSince,
                      Boundary;

        public Packet() {
            Get = new jsObject();
            Post = new jsObject();
            Cookies = new jsObject();
            Route = new jsObject();
            Headers = new jsObject();

            ContentLength = 0;
            RawTarget = Target = Type = Version = Hostname = Connection = Query = ContentType = AcceptEncoding = IfModifiedSince = Boundary = string.Empty;
        }

        public void ProcessHeaders() {
            Headers.ForEach((key, value) => {
                ParseHeader(this, key, value as string);
            });
            
            QueryMembersMatcher.MatchAll(Query, Get);
            PathMatcher.MatchAllValues(Target, Route);
        }

        public static bool Forward(Client In, Client Out) {
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
                        return Stream.Receive(Output.Stream, ContentLength);
                }
                catch { return false; }
            }

            return true;
        }

        public static bool Receive(Client client, Packet recv) {
            string headers;

            try { headers = client.ReceiveStringUntil(DoubleNewLine, Encoding.UTF8); }
            catch { goto closeConnection; }

            return Parse(recv, headers);

        closeConnection:
            client.Dispose();
            return false;
        }

        public static bool Parse(Packet recv, string headers) {
            if (headers == null || headers.Length == 0)
                return false;

            var i = headers.IndexOf('\r');
            if (i == -1 ||
                RequestHeaderMatcher.Match(headers, recv) == null ||
                HeaderMatcher.MatchKeyValuePairs(headers, recv.Headers, i) == null) {
                return false;
            }
            else {
                recv.Headers.ForEach((key, value) => {
                    ParseMinimum(recv, key, value as string);
                });

                i = recv.RawTarget.IndexOf('?');
                if (i == -1) {
                    recv.Target = recv.RawTarget;
                }
                else {
                    recv.Target = recv.RawTarget.Substring(0, i);
                    recv.Query = recv.RawTarget.Substring(i + 1);
                }
            }

            return true;
        }

        private static void ParseMinimum(Packet recv, string Key, string Value) {
            switch (Key) {
                case "Host":
                    HostMatcher.Match(Value, recv);
                    return;

                case "If-Modified-Since":
                    recv.IfModifiedSince = Value;
                    return;

                case "Accept-Encoding":
                    recv.AcceptEncoding = Value;
                    return;
            }
        }

        private static void ParseHeader(Packet recv, string Key, string Value) {
            switch (Key) {
				case "Content-Length":
					long.TryParse(Value, out recv.ContentLength);
					return;

				case "Content-Type":
                    MultipartBoundaryMatcher.Match(Value, recv);
					return;
					
				case "Cookie": 
					HeaderPropertiesMatcher.MatchAll(Value, recv.Cookies);
					return;
            }
        }

        public override bool TryGet(string Key, out object Value) {
            switch (Key) {
                default:
                return base.TryGet(Key, out Value);

                case "Port": Value = Port; break;
                case "ContentLength": Value = ContentLength; break;
                case "Get": Value = Get; break;
                case "Post": Value = Post; break;
                case "Cookies": Value = Cookies; break;
                case "Route": Value = Route; break;
                case "Headers": Value = Headers; break;
                case "RawTarget": Value = RawTarget; break;
                case "Target": Value = Target; break;
                case "Type": Value = Type; break;
                case "Version": Value = Version; break;
                case "Hostname": Value = Hostname; break;
                case "Connection": Value = Connection; break;
                case "Query": Value = Query; break;
                case "ContentType": Value = ContentType; break;
                case "AcceptEncoding": Value = AcceptEncoding; break;
                case "IfModifiedSince": Value = IfModifiedSince; break;
                case "Boundary": Value = Boundary; break;
            }
            return true;
        }

        public override void AssignValue(string Key, object Value) {
            switch (Key) {
                default: base.AssignValue(Key, Value); break;
                case "Port": Port = Convert.ToInt32(Value); break;
                case "ContentLength": ContentLength = Convert.ToInt64(Value); break;
                case "Get": Get = Value as jsObject; break;
                case "Post": Post = Value as jsObject; break;
                case "Cookies": Cookies = Value as jsObject; break;
                case "Route": Route = Value as jsObject; break;
                case "Headers": Headers = Value as jsObject; break;
                case "RawTarget": RawTarget = Value?.ToString(); break;
                case "Target": Target = Value?.ToString(); break;
                case "Type": Type = Value?.ToString(); break;
                case "Version": Version = Value?.ToString(); break;
                case "Hostname": Hostname = Value?.ToString(); break;
                case "Connection": Connection = Value?.ToString(); break;
                case "Query": Query = Value?.ToString(); break;
                case "ContentType": ContentType = Value?.ToString(); break;
                case "AcceptEncoding": AcceptEncoding = Value?.ToString(); break;
                case "IfModifiedSince": IfModifiedSince = Value?.ToString(); break;
                case "Boundary": Boundary = Value?.ToString(); break;
            }
        }
    }
}