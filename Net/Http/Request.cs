using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;
using Poly.Script;

namespace Poly.Net.Http {
    public partial class Request : jsObject {
        public bool Handled {
            get {
                return Get<bool>("Handled", false);
            }
            set {
                Set("Handled", value);
            }
        }

        public Client Client {
            get {
                return Get<Client>("Client", default(Client));
            }
            set {
                Set("Client", value);
            }
        }

        public Packet Packet {
            get {
                return Get<Packet>("Packet", default(Packet));
            }
            set {
                Set("Packet", value);
            }
        }

        public Result Result {
            get {
                return Get<Result>("Result", () => { return "200 Ok"; });
            }
            set {
                Set("Result", value);
            }
        }
        
        public Host Host = null;

        public Session Session {
            get {
                return Get<Session>("Session");
            }
            set {
                Set("Session", value);
            }
        }

        public MemoryStream Data { get; private set; }

        public Request(Client Client, Packet Packet) {
            this.Client = Client;
            this.Packet = Packet;

            Data = new MemoryStream();
        }

        public Data.jsObject Get {
            get {
                return Packet.GET;
            }
        }

        public Data.jsObject Post {
            get {
                return Packet.POST;
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
            var Bytes = Client.Encoding.GetBytes(txt);

            Print(Bytes);
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

        public void SendReply() {
            if (!Client.Connected || Handled)
                return;

            var Type = Result.Headers.Search<string>("Content-Type");

            var Length = this.Data == null ?
                Result.Headers.Search<object>("Content-Length") :
                this.Data.Length;

            if (string.IsNullOrEmpty(Type))
                Type = Result.MIME;

            if (Length == null)
                Length = 0;

            StringBuilder Output = new StringBuilder();
            Output.AppendFormat("HTTP/1.1 {1}{0}Date: {2}{0}Content-Type: {3}{0}Content-Length: {4}{0}", Environment.NewLine, 
                Result.Status, 
                DateTime.UtcNow.HttpTimeString(),
                Type,
                Length
            );

            foreach (var Pair in Result.Headers) {
                Output.AppendFormat("{1}: {2}{0}", Environment.NewLine,
                    Pair.Key,
                    Pair.Value
                );
            }

            foreach (jsObject Opt in Cookies.Values) {
                Output.Append("Set-Cookie: ");

                foreach (var Pair in Opt) {
                    Output.AppendFormat("{0}={1}; ", Pair.Key, Pair.Value);
                }

                Output.AppendLine();
            }

            Client.SendLine(Output.ToString());
        }

        public void Finish() {
            if (Handled)
                return;

            if (Packet.Connection == "keep-alive") {
                Result.Headers["Connection"] = "Keep-Alive";
                Result.Headers["Keep-Alive"] = "timeout=10, max=99";
            }

            var Accept = Packet.Headers["Accept-Encoding"] as string;
            var Compressed = Accept != null && Accept.Contains("gzip") && Data.Length > 0;
            var Stream = Client.GetStream();

            if (Stream == null)
                return;
            
            if (Compressed) {
                var Buffer = new MemoryStream();

                using (var Compress = new GZipStream(Buffer, CompressionMode.Compress, true)) {
                    Data.Position = 0;
                    Data.CopyTo(Compress);
                }

                Result.Headers["Vary"] = "Accept-Encoding";
                Result.Headers["Content-Encoding"] = "gzip";
                Data = Buffer;
            }

            SendReply();

            Data.Position = 0;
            Data.CopyToAsync(Stream);

            Handled = true;
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
