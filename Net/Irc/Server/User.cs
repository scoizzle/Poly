using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Server {
        public partial class User : Irc.User {
            public Server Server = null;
            public Tcp.Client Client = null;

            public DateTime LastPongTime = DateTime.MinValue;
            public string LastPingMessage = "";

            public jsObject<Channel> Channels = new jsObject<Channel>();

            public User(Server Server, Tcp.Client Client) {
                this.Server = Server;
                this.Client = Client;
            }

            public override string ToString(bool HumanFormat) {
                if (string.IsNullOrEmpty(Nick)) {
                    return Ident + "@" + Host;
                }
                return Nick + "!" + Ident + "@" + Host;
            }
        }
    }
}
