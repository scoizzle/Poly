using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    using Tcp;

    public class Packet : jsComplex {
        public static readonly Matcher PathMatcher = new Matcher("/{Value:![/]}"),
                                       QueryMembersMatcher = new Matcher("[&]{Key}={Value:!Whitespace,![&]}"),
                                       HeaderPropertiesMatcher = new Matcher("[; ]{Key}={Value:!Whitepsace}"),
                                       MultipartHeaderPropertiesMatcher = new Matcher("[;] {Key}=\"{Value}\"");

        public int Port;
        public long ContentLength;
        public new jsObject Get;
        public jsObject Headers, Post, Cookies, Route;
        public string Host, RawTarget, Connection, Type, Target, Version, Query, ContentType, Boundary;

        public Packet() {
            Headers = new jsObject();
            Get = new jsObject();
            Post = new jsObject();
            Cookies = new jsObject();
            Route = new jsObject();

            ContentLength = 0;
            Host = RawTarget = Connection = Type = Target = Version = Query = ContentType = Boundary = string.Empty;
        }

        public static async Task<Packet> Receive(Client Client) {
            if (Client.Connected) {
                int Index;
                string FirstLine, CurrentLine;
                Packet Recv = new Packet();

                try {
                    FirstLine = await Client.ReceiveLine();

                    while (!string.IsNullOrEmpty(CurrentLine = await Client.ReceiveLine())) {
                        Index = CurrentLine.IndexOf(':');

                        if (Index == -1) {
                            Client.Close();
                            return null;
                        }

                        Recv.Headers.Set(
                            CurrentLine.Substring(0, Index),
                            CurrentLine.Substring(Index + 2)
                        );
                    }
                }
                catch {
                    return null;
                }

                Index = FirstLine.IndexOf(' ');
                if (Index == -1)
                    return null;

                var Second = FirstLine.IndexOf(' ', Index + 1);
                if (Index == -1 || Second  + 1 == FirstLine.Length)
                    return null;
                
                Recv.Type = FirstLine.Substring(0, Index);
                Recv.RawTarget = FirstLine.Substring(Index + 1, Second - Index - 1);
                Recv.Version = FirstLine.Substring(Second + 1);

                if ((Index = Recv.RawTarget.IndexOf('?')) != -1) {
                    Recv.Query = Recv.RawTarget.Substring(Index + 1);
                    Recv.Target = Uri.UnescapeDataString(Recv.RawTarget.Substring(0, Index));

                    QueryMembersMatcher.MatchAll(Recv.Query, Recv.Get, true);
                }
                else {
                    Recv.Target = Uri.UnescapeDataString(Recv.RawTarget);
                }

                if (!string.IsNullOrEmpty(Recv.Host = Recv.Headers.Get<string>("Host"))) {
                    if ((Index = Recv.Host.IndexOf(':')) != -1) {
                        int.TryParse(Recv.Host.Substring(Index + 1), out Recv.Port);
                        Recv.Host = Recv.Host.Substring(0, Index);
                    }

                    if (Recv.Headers.ContainsKey("Content-Type")) {
                        var Type = Recv.Headers.Get<string>("Content-Type");

						if (Type.StartsWith("multipart/form-data", StringComparison.Ordinal)) {
                            Type = Type.Substring(30);
                            Recv.ContentType = "multipart/form-data";
                            Recv.Boundary = Type;
                        }
                        else {
                            Recv.ContentType = Type;
                        }
                    }

                    if ((Recv.ContentLength = Recv.Headers.Get<long>("Content-Length")) > 0) {
                        if (Recv.Type == "POST") { 
                            switch (Recv.ContentType) {
                                case "application/x-www-form-urlencoded": {
                                    QueryMembersMatcher.MatchAll(await Client.ReceieveString(Recv.ContentLength), Recv.Post, true);
                                    break;
                                }

                                case "multipart/form-data": {
										MultipartHandler Handler = new MultipartHandler(Client, Recv, new BufferedStreamer(Client.GetStream()));

                                        if (!await Handler.Receive())
                                            return null;
                                    break;
                                }
                            }
                        }
                    }

                    if (Recv.Headers.ContainsKey("Cookie")) {
                        HeaderPropertiesMatcher.MatchAll(Recv.Headers.Get<string>("Cookie"), Recv.Cookies, true);
                    }

                    PathMatcher.MatchAll(Recv.Target, Recv.Route, true);
                    return Recv;
                }
            }

            return null;
        }

        public static async Task<bool> Forward(Client In, Client Out) {
            if (In.Connected && Out.Connected) {
                var Stream = In.GetStreamer();
                var Output = Out.GetStreamer();

                var Headers = new StringBuilder();
                long ContentLength = 0;

                try {
                    for (var c = 128; c != 0; c--) {
                        var Line = await In.ReceiveLine();
                        if (Line == null) return false;

                        Headers.Append(Line).Append(App.NewLine);
                        if (Line.Length == 0) break;

                        if (ContentLength == 0) 
                            if (string.Compare(Line, 0, "Content-Length: ", 0, 16, StringComparison.Ordinal) == 0)
                                long.TryParse(Line.Substring(16), out ContentLength);
                    }

                    await Out.Send(Headers.ToString());

                    if (ContentLength > 0)
                        return await Stream.Receive(Output.Stream, ContentLength);
                }
                catch { return false; }
            }

            return true;
        }
    }
}