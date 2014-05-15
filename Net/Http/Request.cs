using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;
using Poly.Script;

namespace Poly.Net.Http {
    public partial class Request : jsObject {
        private int PrintedCount = 0;

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

            PrintedCount += Bytes.Length;
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

            Client.SendLine("HTTP/1.1 ", Result.Status);
            Client.SendLine("Date: ", DateTime.UtcNow.HttpTimeString());

            if (this.Data != null && Data.Length > 0) {
                if (Result.Headers.Search<string>("content-type") == null) {
                    Client.SendLine("Content-Type: ", Result.MIME);
                }
                Client.SendLine("Content-Length: ", Data.Length.ToString());
            }
            else {
                Client.SendLine("Content-Length: 0");
            }

            Result.Headers.ForEach((K, V) => {
                Client.SendLine(K, ": ", V.ToString());
            });

            Result.Cookies.ForEach<jsObject>((K, V) => {
                Client.Send("Set-Cookie: ");

                V.ForEach((OK, OV) => {
                    Client.Send(OK, "=", OV.ToString(), ";");
                });

                Client.SendLine();
            });

            Client.SendLine();
        }

        public void Finish() {
            if (Handled)
                return;

            if (Packet.Connection == "keep-alive") {
                Result.Headers["Connection"] = "Keep-Alive";
                Result.Headers["Keep-Alive"] = "timeout=15, max=99";
            }

            SendReply();

            Data.Position = 0;
            Data.WriteTo(Client.GetStream());
            Data.Close();

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
