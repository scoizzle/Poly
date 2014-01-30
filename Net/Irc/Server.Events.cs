using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Net.Irc {
    public partial class Server {
        public override void OnClientConnect(Tcp.Client Client) {
            var User = new User() {
                Client = Client
            };

            while (Client.Connected) {
                var Packet = new Packet();

                if (Packet.Receive(Client)) {
                    HandlePacket(User, Packet);
                }
            }
        }
    }
}
