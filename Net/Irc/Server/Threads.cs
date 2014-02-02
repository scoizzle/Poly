using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net.Irc {
    public partial class Server {
        public void HandlePacket(User User, Packet Packet) {
            switch (Packet.Action) {
                case "Pong":
                    if (User.LastPingMessage == Packet.Message) {
                        User.LastPongTime = DateTime.Now;

                        if (!User.IsAuthenticated) {
                            User.IsAuthenticated = true;
                        }
                    }
                    break;

				case "User":
					if (User.IsAuthenticated) {
						User.Ident = Packet.getString ("Ident");
						User.IsHidden = (User.getInt ("Visible") == 8);
						User.RealName = Packet.Message;
					}
                    break;

                case "Pass":
                    User.Password = Packet.Message;
                    break;

                case "Nick":
                    User.Nick = Packet.Message;

                    if (!User.IsAuthenticated) {
                        SendPing(User);
                    }
                    break;
            }
        }

        public void PingPong() {
            while (true) {
                var List = this.Users.ToList();
                
                for (int i = 0; i < List.Count; i++) {
                    var User = List[i].Value as User;

                    var Delay = DateTime.Now - User.LastPongTime;

                    if (Delay.TotalSeconds > PingTimeout) {
                        DisconnectUser(User, "Ping timeout.");
                    }
                }
            }
        }
    }
}
