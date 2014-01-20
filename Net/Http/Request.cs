using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        public List<byte> Output = new List<byte>();

        public Request(Client Client, Packet Packet) {
            this.Client = Client;
            this.Packet = Packet;
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

        public void Print(string txt) {
            if (!string.IsNullOrEmpty(txt)) {
                Output.AddRange(Client.Writer.Encoding.GetBytes(txt));
            }
        }

        public void Finish() {
            if (Output.Count > 0) {
                Result.Data = Output.ToArray();
                Output.Clear();
            }
            Handled = true;
        }

        public void SetCookie(string name, string value) {
            Result.Cookies[name, name] = value;
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
