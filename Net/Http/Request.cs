using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;
using Poly.Script;

namespace Poly.Net.Http {
    public partial class Request : jsComplex {
        public Client Client;
        public Packet Packet;
        public Result Result;
        public Host Host;

        public Session Session;

        public Stream Data { get; set; }

        public Request(Client Client, Packet Packet) {
            this.Client = Client;
            this.Packet = Packet;

            this.Result = new Result();
            this.Data = new MemoryStream();
        }

        new public Data.jsObject Get {
            get {
                return Packet.Get;
            }
        }

        public Data.jsObject Post {
            get {
                return Packet.Post;
            }
        }

        public Data.jsObject Cookies {
            get {
                return Packet.Cookies;
            }
        }

        public void Print(byte[] Bytes) {
            if (Bytes == null)
                return;

            Data.Write(Bytes, 0, Bytes.Length);
        }

        public void Print(string txt) {
            Print(Client.Writer.Encoding.GetBytes(txt));
        }

        public void Print(string FileName, jsObject Data) {
            if (File.Exists(FileName)) {
                Print(
                    Data.Template(
                        File.ReadAllText(FileName)
                    )
                );
            }
        }

        public void Load(string FileName) {
            if (File.Exists(FileName)) {
                Print(
                    File.ReadAllText(FileName)
                );
            }

        }

        public async Task Finish() {
            bool GZip, HeadersOnly;
            long ContentLength;
            string Accept, ContentType;

            Data.Position = 0;

            Accept = Packet.Headers.Get<string>("Accept-Encoding") ?? "";
            ContentType = Result.Headers.Get<string>("Content-Type") ?? Result.ContentType;

            HeadersOnly = Packet.Type == "HEAD";
            ContentLength = HeadersOnly ?
                0 : this.Data.Length;

            GZip = !HeadersOnly && Accept.Contains("gzip") && ContentLength > 64;

            if (GZip) {
                Result.Headers["Vary"] = "Accept-Encoding";
                Result.Headers["Content-Encoding"] = "gzip";

                var Buffer = new MemoryStream();

                using (var Compression = new GZipStream(Buffer, CompressionMode.Compress, true)) {
                    Data.CopyTo(Compression);
                }

                Data = Buffer;
                Data.Position = 0;

                ContentLength = Data.Length;
            }

            StringBuilder Headers = new StringBuilder();
            Headers.AppendFormat("HTTP/1.1 {0}\r\nDate: {1}\r\nContent-Type: {2}\r\nContent-Length: {3}\r\n",
                Result.Status,
                DateTime.UtcNow.HttpTimeString(),
                ContentType,
                ContentLength
            );

            foreach (var Pair in Result.Headers) {
                Headers.AppendFormat("{0}: {1}\r\n",
                    Pair.Key,
                    Pair.Value
                );
            }

            foreach (var Pair in Cookies) {
                if (Pair.Value is jsObject) {
                    Headers.Append("Set-Cookie: ");

                    foreach (var P in Pair.Value as jsObject) {
                        Headers.AppendFormat("{0}={1}; ", P.Key, P.Value);
                    }

                    Headers.AppendLine();
                }
            }

            Headers.AppendLine();

            try {
                await Client.Writer.WriteAsync(Headers.ToString());

                if (!HeadersOnly)
                    await Data.CopyToAsync(Client.Stream);
            }
            catch { Client.Close(); }
        }

        public void SetCookie(string name, string value) {
            SetCookie(name, value, 0);
        }

        public void SetCookie(string name, string value, long expire = 0, string path = "", string domain = "", bool secure = false) {
            var Options = new jsObject();

            Options[name] = value;

            if (expire > 0) {
                Options["expire"] = DateTime.UtcNow.AddSeconds(expire).HttpTimeString();
            }

            if (!string.IsNullOrEmpty(path))
                Options["path"] = path;

            if (!string.IsNullOrEmpty(domain))
                Options["domain"] = domain;

            if (secure)
                Options["secure"] = "true";

            Result.Cookies[name] = Options;
        }
    }
}
