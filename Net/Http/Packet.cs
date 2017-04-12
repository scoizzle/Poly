using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
	using Data;
	using Tcp;

    public class Packet {
        static readonly byte[] DoubleNewLine = Encoding.UTF8.GetBytes("\r\n\r\n");

        public bool Gzip;
        public string Version;
        public Client Client;
        public KeyValueCollection<string> Headers;
        public ContentHandler Body;
        
        public Packet(Client client) {
            Client = client;
            Headers = new KeyValueCollection<string>();
            Body = new ContentHandler(this);
        }

        public string Date {
            get {
                return Headers["Date"];
            }
            set {
                Headers["Date"] = value;
            }
        }

        public long ContentLength {
            get {
                var str = Headers["Content-Length"];
                if (string.IsNullOrEmpty(str)) return 0;

                return Convert.ToInt64(str);
            }
            set {
                Headers["Content-Length"] = value.ToString();
            }
        }

        public string ContentType {
            get {
                return Headers["Content-Type"];
            }
            set {
                Headers["Content-Type"] = value;
            }
        }

        public string LastModified {
            get { return Headers["Last-Modified"]; }
            set { Headers["Last-Modified"] = value; }
        }

        public Stream Content {
            get { return Body.Content; }
            set { Body.Content = value; }
        }

        public Task<bool> Send() {
            return Client.Send(ToString());
        }

        public async Task<bool> Receive() {
            var headers = await Client.ReceiveStringUntilConstrained(DoubleNewLine);

            if (string.IsNullOrEmpty(headers))
                return false;

            return ParseHeaders(headers);
        }

        public virtual void Reset() {
            Version = string.Empty;
            Headers.Clear();
            Body.Reset();
        }

        public override string ToString() {
            var Output = new StringBuilder();

            GenerateHeaders(Output);

            return Output.ToString();
        }

        internal virtual bool ParseHeaders(StringIterator It) {
            string Key, Value, Prev;

            while (!It.IsDone()) {
                Key = It.Extract(": ");

                if (Key == null)
                    return false;

                Value = It.Extract(App.NewLine);

                if (Value == null) {
                    Value = It.ToString();
                    It.ConsumeSection();
                }
                
                if (Headers.TryGetValue(Key, out Prev))
                    Headers.Set(Key, Prev + Value);
                else
                    Headers.Set(Key, Value);
            }

            return true;
        }

        internal virtual void GenerateHeaders(StringBuilder Output) {
            foreach (var Pair in Headers) {
                Output.Append(Pair.Key).Append(": ")
                      .Append(Pair.Value).Append(App.NewLine);
            }
            Output.Append(App.NewLine);
        }
    }
}