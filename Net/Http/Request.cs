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
        public Host Host;
        public Client Client;
        public Packet Packet;
        public Result Result;

        public new jsObject Get;
        public jsObject Post, Cookies;

        public bool CompressionEnabled,
                    HeadersOnly;

        public Stream Data { get; set; }
        public StringBuilder OutputBuilder;

        public Request(Client Client, Packet Packet, Host Host) {
            this.Host = Host;
            this.Client = Client;
            this.Packet = Packet;

            this.Get = Packet.Get;
            this.Post = Packet.Post;
            this.Cookies = Packet.Cookies;


            Result = new Result();
            OutputBuilder = new StringBuilder();

            HeadersOnly = string.Compare(Packet.Type, "HEAD", StringComparison.Ordinal) == 0;

            if (Packet != null && 
                Packet.Headers.Get<string>("Accept-Encoding")?.Contains("gzip") == true)
                {
                    CompressionEnabled = !HeadersOnly;
                }
            else {
                CompressionEnabled = false;
            }
        }

        public void Print(string txt) {
            OutputBuilder.Append(txt);
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
            long ContentLength;

            if (Data == null) {
                Data = new MemoryStream(Encoding.Default.GetBytes(OutputBuilder.ToString()));
            }
            else if (Data is FileStream) {
                CompressionEnabled = false;
            }
            else { 
                Data.Position = 0;
            }
            ContentLength = HeadersOnly ?
                0 : this.Data.Length;

            try {
                if (CompressionEnabled && ContentLength > 512) { 
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
                Headers.Append("HTTP/1.1 ").Append(Result.Status)
                       .Append("\r\nDate: ").Append(DateTime.UtcNow.HttpTimeString())
                       .Append("\r\nContent-Type: ").Append(Result.ContentType)
                       .Append("\r\nContent-Length: ").Append(ContentLength.ToString()).Append(App.NewLine);

                foreach (var Pair in Result.Headers) {
                    Headers.Append(Pair.Key).Append(": ").Append(Pair.Value.ToString()).Append(App.NewLine);
                }

                foreach (var Pair in Result.Cookies) {
                    if (Pair.Value is jsObject) {
                        Headers.Append("Set-Cookie: ");

                        foreach (var P in Pair.Value as jsObject) {
                            Headers.Append(P.Key).Append("=").Append(P.Value).Append("; ");
                        }

                        Headers.Append(App.NewLine);
                    }
                }

                Headers.Append(App.NewLine);

                if (Client.Connected) {
                    await Client.Send(Headers.ToString());

                    if (!HeadersOnly) {
                        await Data.CopyToAsync(Client.Stream.Stream);
                    }
                }
            }
            catch (Exception Error) {
                App.Log.Error(Error.StackTrace);
                Client.Close();
            }
        }

        public void SetCookie(string name, string value) {
            SetCookie (name, value, 0);
        }

		public void SetCookie(string name, string value, string path) {
			SetCookie (name, value, 0, path);
		}

		public void SetCookie(string name, string value, string path, string domain) {
			SetCookie (name, value, 0, path, domain);
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
