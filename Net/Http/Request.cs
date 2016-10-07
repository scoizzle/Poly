using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Threading;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;
using Poly.Script;

namespace Poly.Net.Http {
    public partial class Request : Packet {
        public Server Host;
        public Client Client;
        public Result Result;

        public bool CompressionEnabled,
                    HeadersOnly;

        public string HeaderString {
            get {
                return Result.GetResponseString();
            }
        }

        public StringBuilder OutputBuilder;

        public Request(Client client) {
            Client = client;
            Result = new Result();
            OutputBuilder = new StringBuilder();
        }

        public void Print(string Content) {
            OutputBuilder.Append(Content);
        }

        public void Prepare() {
            HeadersOnly = string.Compare(Type, "HEAD", StringComparison.Ordinal) == 0;
            CompressionEnabled = AcceptEncoding.Find("gzip") != -1;
        }

        public void SendFile(string FileName) {
            Cache.Item Cached = null;

            if (!Host.Cache.TryGetValue(FileName, out Cached)) {
                Result.Status = Result.NotFound;
                return;
            }

            SendFile(Cached);
        }

        public void SendFile(Cache.Item Cached) {
            if (Cached.LastWriteTime == IfModifiedSince) {
                Result.Status = Result.NotModified;
            }
            else {
                Result.Headers["Last-Modified"] = Cached.LastWriteTime;
                Result.ContentType = Cached.ContentType;

                if (Cached.IsCompressed) {
                    if (CompressionEnabled) {
                        Result.Headers["Vary"] = "Accept-Encoding";
                        Result.Headers["Content-Encoding"] = "gzip";
                    }
                }

                Result.Content = Cached.GetContent(CompressionEnabled);
            }
        }

        public void Finish() {
            try {
                var In = Result.Content;
                var Out = Client.Stream.Stream;
                var Headers = HeaderString;
                var Buffer = new byte[Host.SendBufferSize];
                var Offset = Encoding.UTF8.GetBytes(Headers, 0, Headers.Length, Buffer, 0);

                if (HeadersOnly || In == null) {
                    Out.Write(Buffer, 0, Offset);
                }
                else {
                    var ToSend = In.Length;
                    var Read = In.Read(Buffer, Offset, Buffer.Length - Offset);

                    Out.Write(Buffer, 0, Offset + Read);
                    ToSend -= Read;
                    
                    while (ToSend > 0 && Client.Connected) {
                        Read = In.Read(Buffer, 0, (int)Math.Min(Buffer.Length, ToSend));

                        if (Read == 0)
                            break;

                        Out.Write(Buffer, 0, Read);
                        ToSend -= Read;
                        Thread.Sleep(0);
                    }
                }

                Buffer = null;
            }
            catch { }
        }

        public bool ReceiveContent() {
            if (ContentLength > 0) {
                switch (ContentType) {
                    case "application/x-www-form-urlencoded":
                    QueryMembersMatcher.MatchAll(Client.ReceiveString(ContentLength), Post);
                    break;

                    case "multipart/form-data": {
                        MultipartHandler Handler = new MultipartHandler(Client, this);

                        if (!Handler.Receive())
                            return false;

                        break;
                    }
                }
            }
            return true;
        }
    }
}
