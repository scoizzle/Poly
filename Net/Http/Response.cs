using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    using Tcp;

    public class Response : Packet {
        public string Status;

        public Response(Client client) : base(client) {
            Status = Result.Ok;
        }

        public override void Reset() {
            Status = string.Empty;

            base.Reset();
        }

        internal override bool ParseHeaders(StringIterator It) {
            Version = It.Extract(' ');
            Status = It.Extract(App.NewLine);

            if (Version == null || Status == null)
                return false;

            return base.ParseHeaders(It);
        }

        internal override void GenerateHeaders(StringBuilder Output) {
            Output.Append(Version).Append(' ')
                  .Append(Status).Append(App.NewLine);

            base.GenerateHeaders(Output);
        }
    }
}
