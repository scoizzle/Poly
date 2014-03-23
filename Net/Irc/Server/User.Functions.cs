using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Server {
        public partial class User : Irc.User {
            public void Send(params Packet[] Packets) {
                for (int i = 0; i < Packets.Length; i++) {
                    Packets[i].Send(Client);
                }
            }

            public void Disconnect(string Message) {
                Channels.ForEach<Channel>((Name, Channel) => {
                    if (Channel.Users.ContainsKey(Nick)) {
                        Channel.OnDisconnect(this, Message);
                    }
                });

                Client.Close();
            }

            public void Ping() {
                var Message = DateTime.Now.GetHashCode().ToString();

                Send(new Packet("OnPing") {
                    { "Message", Message }
                });

                LastPingMessage = Message;
                LastPongTime = DateTime.Now;
            }
        }
    }
}
