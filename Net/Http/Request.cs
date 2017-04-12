using System;
using System.IO;
using System.Text;

namespace Poly.Net.Http {
    using Tcp;

    public class Request : Packet {
        public string Method,
                      Target,
                      Query;

        public bool HeadersOnly;
        
        public Request(Client client) : base(client) {
            Body.Content = new MemoryStream();
        }
        
        public string Item {
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

        public override void Reset() {
            Method = Target = Query = string.Empty;
            HeadersOnly = false;

            base.Reset();
        }

        internal override bool ParseHeaders(StringIterator It) {
            Method = It.Extract(' ');
            Target = It.Extract(' ');
            Version = It.Extract(App.NewLine);

            if (Method == null || Item == null || Version == null)
                return false;

            Item = Target;

            if (base.ParseHeaders(It)) {
                if (Headers.TryGetValue("Accept-Encoding", out string AcceptEncoding))
                    Gzip = AcceptEncoding.Find("gzip") != -1;

                HeadersOnly = Method.Compare("HEAD");
                return true;
            }
            return false;
        }

        internal override void GenerateHeaders(StringBuilder Output) {
            Output.Append(Method).Append(' ')
                  .Append(Item).Append(' ')
                  .Append(Version).Append(App.NewLine);

            base.GenerateHeaders(Output);
        }
    }
}
