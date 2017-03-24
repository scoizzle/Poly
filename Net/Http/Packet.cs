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

    public class Packet {
        public static readonly byte[] DoubleNewLine = Encoding.UTF8.GetBytes("\r\n\r\n");

        public string Method,
                      Version,
                      Target,
                      Query;

        public JSON<string> Headers;

        public Packet() {
            Headers = new JSON<string>();
        }

        public long ContentLength {
            get {
                return Convert.ToInt64(Headers["Content-Length"]);
            }
        }

        public string Request {
            get {
                return Target + ((Query?.Length > 0) ? '?' + Query : string.Empty);
            }
            set {
                var i = value.IndexOf('?');

                if (i == -1) {
                    Target = value;
                    Query = string.Empty;
                }
                else {
                    Target = value.Substring(0, i);
                    Query = value.Substring(i + 1);
                }
            }
        }

        public async Task<bool> Receive(Client client) {
            int x, y;
            string headers, Key, Value;

            if (!await client.ReadyToRead()) return false;
            
            try { headers = await client.ReceiveStringUntilConstrained(DoubleNewLine, Encoding.UTF8); }
            catch { 
                client.Close();
                return false;
            }

            if (headers == null || headers.Length == 0)
                return false;

            y = headers.IndexOf(' ');
            Method = headers.Substring(0, y);
            x = y + 1;

            y = headers.IndexOf(' ', x);
            Request = headers.Substring(x, y - x);
            x = y + 1;

            y = headers.IndexOf('\r', x);
            Version = headers.Substring(x, y - x);
            x = y + 2;

            while (x < headers.Length) {
                y = headers.IndexOf(':', x);
                if (y == -1) break;

                Key = headers.Substring(x, y - x);

                x = y + 2;
                y = headers.IndexOf('\r', x);

                if (y == -1)
                    y = headers.Length;

                Value = headers.Substring(x, y - x);
                x = y + 2;

                Headers.Set(Key, Value);
            }

            return true;
        }

        public void Reset() {
            Method = Version = Target = Query = string.Empty;
            Headers.Clear();
        }
    }
}

    /*public class Packet : jsComplex
    {
        public static readonly Matcher PathMatcher = new Matcher("/{Value:![/]}"),
                                       HostMatcher = new Matcher("{Hostname:![:]}[:{Port: Numeric -> Int}]"),
                                       QueryMembersMatcher = new Matcher("[&]{Key}={Value:!Whitespace,![&]}"),
                                       HeaderPropertiesMatcher = new Matcher("[; ]{Key}={Value:!Whitepsace}"),
                                       MultipartHeaderPropertiesMatcher = new Matcher("[;] {Key}=\"{Value}\""),
                                       MultipartBoundaryMatcher = new Matcher("{ContentType}; boundary={Boundary}");

        public static readonly byte[] DoubleNewLine = Encoding.UTF8.GetBytes("\r\n\r\n");

        public int Port;
        public long ContentLength;
        public new JSON Get;
        public JSON Post, Cookies, Route, Headers;
        public string Request,
                      Target,
                      Method,
                      Version,
                      Hostname,
                      Connection,
                      Query,
                      ContentType,
                      AcceptEncoding,
                      IfModifiedSince,
                      Boundary;

        public Packet()
        {
            Get = new JSON();
            Post = new JSON();
            Cookies = new JSON();
            Route = new JSON();
            Headers = new JSON();

            ContentLength = 0;
            Request = Target = Method = Version = Hostname = Connection = Query = ContentType = AcceptEncoding = IfModifiedSince = Boundary = string.Empty;
        }

        public void ProcessHeaders()
        {
            Headers.ForEach((key, value) =>
            {
                ParseHeader(this, key, value as string);
            });

            QueryMembersMatcher.MatchAll(Query, Get);
            PathMatcher.MatchAllValues(Target, Route);
        }

        public static bool Forward(Client In, Client Out)
        {
            if (In.Connected && Out.Connected)
            {
                var Stream = In.GetStreamer();
                var Output = Out.GetStreamer();

                var Headers = new StringBuilder();
                long ContentLength = 0;

                try
                {
                    for (var c = 128; c != 0; c--)
                    {
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

        public static bool Receive(Client client, Packet recv)
        {
            string headers;

            try { headers = client.ReceiveStringUntil(DoubleNewLine, Encoding.UTF8); }
            catch { goto closeConnection; }

            return Parse(recv, headers);

        closeConnection:
            client.Dispose();
            return false;
        }

        public static bool Parse(Packet recv, string headers)
        {
            int x = 0, y = 0;

            y = headers.IndexOf(' ', x);
            recv.Method = headers.Substring(x, y - x);
            x = y + 1;

            y = headers.IndexOf(' ', x);
            recv.Request = headers.Substring(x, y - x);
            x = y + 1;

            y = headers.IndexOf('\r', x);
            recv.Version = headers.Substring(x, y - x);
            x = y + 2;

            while (x < headers.Length)
            {
                y = headers.IndexOf(':', x);
                if (y == -1) break;

                var Key = headers.Substring(x, y - x);

                x = y + 2;
                y = headers.IndexOf('\r', x);

                if (y == -1)
                    y = headers.Length;

                var Value = headers.Substring(x, y - x);
                x = y + 2;

                recv.Headers.Set(Key, Value);
                ParseMinimum(recv, Key, Value);
            }

            x = recv.Request.IndexOf('?');
            if (x == -1)
            {
                recv.Target = recv.Request;
            }
            else
            {
                recv.Target = recv.Request.Substring(0, x);
                recv.Query = recv.Request.Substring(x + 1);
            }
            return true;
        }

        private static void ParseMinimum(Packet recv, string Key, string Value)
        {
            switch (Key)
            {
                case "Host":
                    {
                        int index = Value.IndexOf(':');

                        if (index > 0)
                        {
                            recv.Hostname = Value.Substring(0, index);
                            int.TryParse(Value.Substring(index + 1), out recv.Port);
                        }
                        else
                        {
                            recv.Hostname = Value;
                        }
                        return;
                    }

                case "If-Modified-Since":
                    recv.IfModifiedSince = Value;
                    return;

                case "Accept-Encoding":
                    recv.AcceptEncoding = Value;
                    return;
            }
        }

        private static void ParseHeader(Packet recv, string Key, string Value)
        {
            switch (Key)
            {
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

        public override bool TryGet(string Key, out object Value)
        {
            switch (Key)
            {
                default:
                    return base.TryGet(Key, out Value);

                case "Port": Value = Port; break;
                case "ContentLength": Value = ContentLength; break;
                case "Get": Value = Get; break;
                case "Post": Value = Post; break;
                case "Cookies": Value = Cookies; break;
                case "Route": Value = Route; break;
                case "Headers": Value = Headers; break;
                case "Request": Value = Request; break;
                case "Target": Value = Target; break;
                case "Method": Value = Method; break;
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

        public override void AssignValue(string Key, object Value)
        {
            switch (Key)
            {
                default: base.AssignValue(Key, Value); break;
                case "Port": Port = Convert.ToInt32(Value); break;
                case "ContentLength": ContentLength = Convert.ToInt64(Value); break;
                case "Get": Get = Value as JSON; break;
                case "Post": Post = Value as JSON; break;
                case "Cookies": Cookies = Value as JSON; break;
                case "Route": Route = Value as JSON; break;
                case "Headers": Headers = Value as JSON; break;
                case "Request": Request = Value?.ToString(); break;
                case "Target": Target = Value?.ToString(); break;
                case "Method": Method = Value?.ToString(); break;
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
*/